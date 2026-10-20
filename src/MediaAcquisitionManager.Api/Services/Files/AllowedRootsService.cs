using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <inheritdoc cref="IAllowedRootsService"/>
/// <remarks>
/// <para>
/// MAM-9 — a setting in the database rather than an environment variable, the owner's choice: the
/// folders are edited where the rest of the installation is, and a change takes effect at the next
/// inspection without a restart. A root still has to be a folder the container mounts; the page says so.
/// </para>
/// <para>
/// MAM-24 — the replacement switch and ticks are saved here too, by the page's one Save (ADR 0033), and read through
/// <see cref="IReplacementGates"/>, the read the Download dialog and the send use, so the page can't show a folder's
/// tick differently from how they judge it.
/// </para>
/// <para>
/// MAM-26 — the page also says, per library folder whose tick counts, whether a rename into the quarantine can work there
/// (<see cref="IFileQuarantine.DescribeRenamePlacesAsync"/>, one look for all of them), and how much room each quarantine
/// takes, from the replacements' own rows; and a Save that would leave a quarantine holding names outside every allowed
/// root is refused, so Restore and Delete for good stay within reach (ADR 0030).
/// </para>
/// <para>
/// MAM-39 — where the app's user may not write, the same look also says which folder that is, whose it is and whom the
/// app runs as, so the page can say which PUID and PGID to set.
/// </para>
/// </remarks>
public sealed class AllowedRootsService(
    ApplicationDbContext db,
    IReplacementGates replacementGates,
    IFileQuarantine quarantine,
    IAuditTrail auditTrail,
    ILogger<AllowedRootsService> logger) : IAllowedRootsService
{
    /// <summary>
    /// MAM-26 — the states of a name that sits in a quarantine, or may: there, or in the middle of a write into it, out of
    /// it or in it. What the room counts and what a Save must keep within reach. MAM-40 — a removal's names' as a
    /// replacement's.
    /// </summary>
    private static readonly IReadOnlyList<ReplacementNameState> HeldStates =
    [
        ReplacementNameState.Quarantined,
        ReplacementNameState.Moving,
        ReplacementNameState.Restoring,
        ReplacementNameState.Deleting,
    ];

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);

    public async Task<AllowedRootsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var gates = await replacementGates.ReadAsync(cancellationToken);
        var renamePlaces = await RenamePlacesAsync(gates, cancellationToken);
        // The stale ticks among the folders, by app path: they can't share one with a folder, which would have
        // matched them.
        IReadOnlyList<ReplacementFolderDto> folders =
        [
            .. gates.Folders
                .Select(folder => FolderOf(folder, renamePlaces.GetValueOrDefault(folder.AppPath.ToString())))
                .Concat(gates.StaleTicks.Select(path => new ReplacementFolderDto(path, [], true, ReplacementFolderStatus.NoLongerALibraryFolder)))
                .OrderBy(folder => folder.AppPath, StringComparer.Ordinal),
        ];
        return new AllowedRootsResponse(
            gates.AllowedRoots, gates.IsEnabled, folders, gates.UnmappedLibraryFolderCount, await QuarantinesAsync(cancellationToken));
    }

    public async Task<AllowedRootsUpdateResult> UpdateAsync(
        IReadOnlyList<string?> paths,
        bool replacementEnabled,
        IReadOnlyList<string?> replacementFolders,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (AllowedRootRules.Validate(paths, AllowedRootRules.PlatformStyle) is { } error)
            return AllowedRootsUpdateResult.Invalid(error);
        if (ReplacementFolderRules.Validate(replacementFolders) is { } folderError)
            return AllowedRootsUpdateResult.Invalid(folderError);

        var roots = AllowedRootRules.Normalize(paths);
        var ticks = ReplacementFolderRules.Normalize(replacementFolders);
        // MAM-4's rule: a save that changes nothing records nothing — the same folders in the same order,
        // however they were typed, write no rows and no audit row claiming a change. MAM-24 — the switch and the
        // ticks count too. Only a fast path: the save below decides again under the write lock.
        if (!ChangesOf(await replacementGates.ReadAsync(cancellationToken), roots, replacementEnabled, ticks).Any)
            return AllowedRootsUpdateResult.Saved;

        var actor = await db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

        // One transaction: the old list goes, the new one and its audit row come, or nothing changes.
        // MAM-24 — and EF's BeginTransaction on SQLite is BEGIN IMMEDIATE, so the stored state is read again here under
        // the write lock: two first saves racing can't both insert the switch row (a primary-key clash, a 500), and a
        // save that finds its change already made by the other writes nothing.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var stored = await replacementGates.ReadAsync(cancellationToken);
        // MAM-24 — the ticks the page can offer: a library folder the mappings bring into the app, inside the allowed
        // roots or not (ADR 0033: one outside them only loses its tick's effect), or a tick already stored, which
        // stays ticked until the owner unticks it. Never a path the app doesn't list: a tick is the owner's word on a
        // folder the page showed, not a way to name any folder of the host.
        if (UnknownTick(ticks, stored) is { } unknown)
            return AllowedRootsUpdateResult.Invalid(Message.Of("files.replacement-folders.unknown-folder", ("path", unknown)));

        var changes = ChangesOf(stored, roots, replacementEnabled, ticks);
        if (!changes.Any)
            return AllowedRootsUpdateResult.Saved;

        // MAM-26, ADR 0030 — a quarantine holding names must stay inside an allowed root, or Restore and Delete for good
        // couldn't reach it and the room it takes would be out of sight. Read here, under the write lock, so a move's
        // intent can't slip in between the check and the save; and the mover reads the roots under the same lock in each
        // name's intent, so a Save committed before an intent is seen there and stops the move, and one after it finds the
        // name Moving here. A parent of the old root (/ for /media) keeps it in reach.
        if (changes.Roots && await StrandedQuarantineAsync(roots, cancellationToken) is { } stranded)
        {
            return AllowedRootsUpdateResult.Invalid(Message.Of("files.allowed-roots.quarantine-holds-copies", ("path", stranded)));
        }

        // No target on any row: the roots, the switch and the ticks belong to the installation, not to a row the
        // audit could name.
        if (changes.Roots)
        {
            // The delete is the roots' first write, so the unique index never sees an old row beside a new one.
            await db.AllowedRoots.ExecuteDeleteAsync(cancellationToken);
            db.AllowedRoots.AddRange(roots.Select((path, position) => new AllowedRootEntity { Path = path, Position = position }));
            auditTrail.Record(AuditAction.AllowedRootsChanged, actor, target: null);
        }

        if (changes.Switch)
        {
            // No row is off, so the first switch-on inserts it; after that it is updated, never deleted.
            if (await db.ReplacementSettings.SingleOrDefaultAsync(settings => settings.Id == ReplacementSettingsEntity.SingletonId, cancellationToken) is { } settings)
                settings.IsEnabled = replacementEnabled;
            else
                db.ReplacementSettings.Add(new ReplacementSettingsEntity { IsEnabled = replacementEnabled });
            auditTrail.Record(replacementEnabled ? AuditAction.ReplacementSwitchedOn : AuditAction.ReplacementSwitchedOff, actor, target: null);
        }

        if (changes.Ticks)
        {
            await db.ReplacementFolders.ExecuteDeleteAsync(cancellationToken);
            db.ReplacementFolders.AddRange(ticks.Select((path, position) => new ReplacementFolderEntity { Path = path, Position = position }));
            auditTrail.Record(AuditAction.ReplacementFoldersChanged, actor, target: null);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Information: counts and the switch only, never the paths, which say how the host is laid out.
        if (changes.Roots)
            logger.LogInformation("{ActorUserName} changed the allowed roots; the number allowed is now {Count}.", actor.UserName, roots.Count);
        if (changes.Switch)
            logger.LogInformation("{ActorUserName} switched replacement {Switch}.", actor.UserName, replacementEnabled ? "on" : "off");
        if (changes.Ticks)
            logger.LogInformation("{ActorUserName} changed the library folders ticked for replacement; the number ticked is now {Count}.", actor.UserName, ticks.Count);
        return AllowedRootsUpdateResult.Saved;
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MAM-26, ADR 0033 — for every library folder whose tick counts where it is (<see cref="ReplacementFolderStatus.Available"/>),
    /// what keeps a rename into its allowed root's quarantine from working there, by app path: one look through the write
    /// contract for all of them — the folders themselves, never their files —, and none at all while there is no such
    /// folder. A hint for the owner; the rename itself decides. MAM-39 — with, where the app's user may not write, whose
    /// folder it is.
    /// </summary>
    private async Task<Dictionary<string, RenamePlace>> RenamePlacesAsync(ReplacementGateState gates, CancellationToken cancellationToken)
    {
        List<string> available = [.. gates.Folders.Where(folder => folder.Status == ReplacementFolderStatus.Available).Select(folder => folder.AppPath.ToString())];
        if (available.Count == 0)
            return new Dictionary<string, RenamePlace>(StringComparer.Ordinal);

        // One answer per folder asked, in the order asked: paired by position, never by the text it echoes.
        var places = await quarantine.DescribeRenamePlacesAsync(available, gates.AllowedRoots, cancellationToken);
        return available
            .Zip(places, (folder, place) => (Folder: folder, Place: place))
            .ToDictionary(pair => pair.Folder, pair => pair.Place, StringComparer.Ordinal);
    }

    /// <summary>
    /// One library folder as the page lists it, with what the look found there (MAM-26) — <c>null</c> for a folder it
    /// didn't look at — and (MAM-39) whose folder the app's user may not write to, only where the look said so.
    /// </summary>
    private static ReplacementFolderDto FolderOf(ReplacementGateFolder folder, RenamePlace? place) =>
        new(
            folder.AppPath.ToString(),
            folder.LibraryNames,
            folder.Ticked,
            folder.Status,
            place?.Problems,
            place?.WriteAccess is { } access
                ? new ReplacementFolderWriteAccessDto(access.FolderAppPath, access.OwnerUserId, access.OwnerGroupId, access.AppUserId, access.AppGroupId)
                : null);

    /// <summary>
    /// MAM-26, ADR 0030 — every quarantine holding a replacement's names now, with how many replacements and names and
    /// their sizes as each was before its move, by path (ordinal): from the rows, never a walk of the disk, which the scan
    /// keeps out of the quarantine anyway. MAM-40 — a removal's names count among the names and the room.
    /// </summary>
    private async Task<IReadOnlyList<QuarantineSpaceDto>> QuarantinesAsync(CancellationToken cancellationToken)
    {
        var replaced = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => EF.Constant(HeldStates).Contains(name.State))
            .Select(name => new { name.AllowedRoot, ReplacementJobId = (Guid?)name.ReplacementJobId, CopyRemovalId = (Guid?)null, name.SizeBytes })
            .ToListAsync(cancellationToken);
        // MAM-40 — the owner's removals of a duplicate copy wait in the same quarantine: their names take room there too,
        // counted with the replacements' names, and the removals are counted apart from the replacements.
        var removed = await db.CopyRemovalNames
            .AsNoTracking()
            .Where(name => EF.Constant(HeldStates).Contains(name.State))
            .Select(name => new { name.AllowedRoot, ReplacementJobId = (Guid?)null, CopyRemovalId = (Guid?)name.CopyRemovalId, name.SizeBytes })
            .ToListAsync(cancellationToken);
        return
        [
            .. replaced.Concat(removed)
                .GroupBy(name => name.AllowedRoot, StringComparer.Ordinal)
                .Select(root => new QuarantineSpaceDto(
                    QuarantineOf(root.Key),
                    root.Select(name => name.ReplacementJobId).OfType<Guid>().Distinct().Count(),
                    root.Select(name => name.CopyRemovalId).OfType<Guid>().Distinct().Count(),
                    root.Count(),
                    root.Sum(name => name.SizeBytes)))
                .OrderBy(space => space.Path, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// MAM-26 — the first quarantine holding names whose allowed root no root of <paramref name="roots"/> holds, by path
    /// (ordinal), or <c>null</c>: the rule Restore and Delete for good reach a quarantine by, the old root itself or a
    /// parent of it — never the quarantine itself taken as a root, inside which no quarantine is ever found. A stored root
    /// that no longer parses can't be shown to be in reach, so it is named as it is: fail closed.
    /// </summary>
    private async Task<string?> StrandedQuarantineAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken)
    {
        // MAM-40 — a removal's names count as a replacement's: Restore and Delete for good reach them the same way.
        var heldRoots = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => EF.Constant(HeldStates).Contains(name.State))
            .Select(name => name.AllowedRoot)
            .Union(db.CopyRemovalNames
                .AsNoTracking()
                .Where(name => EF.Constant(HeldStates).Contains(name.State))
                .Select(name => name.AllowedRoot))
            .ToListAsync(cancellationToken);
        List<NormalizedPath> allowed = [.. roots.Select(root => NormalizedPath.TryParse(root, out var path, out _) ? path : null).OfType<NormalizedPath>()];
        return heldRoots
            .Where(root => !NormalizedPath.TryParse(root, out var path, out _) || !allowed.Any(path.IsUnder))
            .Select(QuarantineOf)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The quarantine of the allowed root <paramref name="allowedRoot"/>, as a name stores it: its
    /// <c>.mam-quarantine</c>; the root as it is when it no longer parses.</summary>
    private static string QuarantineOf(string allowedRoot) =>
        NormalizedPath.TryParse(allowedRoot, out var root, out _) ? QuarantineFolder.Of(root).ToString() : allowedRoot;

    /// <summary>
    /// Which parts of the page <paramref name="stored"/> differs from: the roots in order, the switch, and the ticks as
    /// a set — the order of the ticks means nothing, so a list sent in another order is no change.
    /// </summary>
    private static SavedChanges ChangesOf(ReplacementGateState stored, IReadOnlyList<string> roots, bool replacementEnabled, IReadOnlyList<string> ticks) =>
        new(
            !stored.AllowedRoots.SequenceEqual(roots, StringComparer.Ordinal),
            stored.IsEnabled != replacementEnabled,
            !stored.Ticks.ToHashSet(StringComparer.Ordinal).SetEquals(ticks));

    /// <summary>The first of <paramref name="ticks"/> that is neither a library folder of <paramref name="stored"/> nor a
    /// tick already stored, or <c>null</c>.</summary>
    private static string? UnknownTick(IReadOnlyList<string> ticks, ReplacementGateState stored)
    {
        var storedTicks = stored.Ticks
            .Select(tick => NormalizedPath.TryParse(tick, out var path, out _) ? path : null)
            .OfType<NormalizedPath>()
            .ToList();
        foreach (var tick in ticks)
        {
            // Normalised from a path the rules parsed, so it parses again.
            var path = NormalizedPath.TryParse(tick, out var parsed, out _) ? parsed : null;
            if (path is null
                || (!stored.Folders.Any(folder => folder.AppPath.IsSameAs(path)) && !storedTicks.Any(storedTick => storedTick.IsSameAs(path))))
                return tick;
        }

        return null;
    }

    /// <summary>Which parts of the page a save changes.</summary>
    private sealed record SavedChanges(bool Roots, bool Switch, bool Ticks)
    {
        public bool Any => Roots || Switch || Ticks;
    }
}
