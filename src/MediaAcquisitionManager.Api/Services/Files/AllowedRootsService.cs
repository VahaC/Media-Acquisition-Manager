using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
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
/// </remarks>
public sealed class AllowedRootsService(
    ApplicationDbContext db,
    IReplacementGates replacementGates,
    IAuditTrail auditTrail,
    ILogger<AllowedRootsService> logger) : IAllowedRootsService
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.AllowedRoots
            .AsNoTracking()
            .OrderBy(root => root.Position)
            .Select(root => root.Path)
            .ToListAsync(cancellationToken);

    public async Task<AllowedRootsResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var gates = await replacementGates.ReadAsync(cancellationToken);
        // The stale ticks among the folders, by app path: they can't share one with a folder, which would have
        // matched them.
        IReadOnlyList<ReplacementFolderDto> folders =
        [
            .. gates.Folders
                .Select(folder => new ReplacementFolderDto(folder.AppPath.ToString(), folder.LibraryNames, folder.Ticked, folder.Status))
                .Concat(gates.StaleTicks.Select(path => new ReplacementFolderDto(path, [], true, ReplacementFolderStatus.NoLongerALibraryFolder)))
                .OrderBy(folder => folder.AppPath, StringComparer.Ordinal),
        ];
        return new AllowedRootsResponse(gates.AllowedRoots, gates.IsEnabled, folders, gates.UnmappedLibraryFolderCount);
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
            return AllowedRootsUpdateResult.Invalid($"“{unknown}” isn't a library folder the app knows, so it can't be ticked for replacement.");

        var changes = ChangesOf(stored, roots, replacementEnabled, ticks);
        if (!changes.Any)
            return AllowedRootsUpdateResult.Saved;

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
