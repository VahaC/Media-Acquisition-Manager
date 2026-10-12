using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-40 — the owner's removal of a duplicate copy as an earlier request left it, planted directly in the database and on
/// the fake disk over <see cref="ReplacementVerificationHarness"/>'s world, for the HTTP tests of what the owner does next
/// (Restore, Delete for good, the list) and of a restart's recovery: the removal, its names in the states given, and each
/// file where its state says it is — in the quarantine at <see cref="QuarantinePathOf"/>, or at its old path —, every old
/// folder still there. Its reads give back the removal's rows and audit rows as stored.
/// </summary>
/// <remarks>
/// The Remove itself is tested by removing, over real HTTP; what the owner does next stands on rows a removal would have
/// written, so a test of Restore doesn't hang on every rule of Remove.
/// </remarks>
public sealed class CopyRemovalPlanter(MediaAcquisitionManagerApiFactory factory, ReplacementVerificationHarness harness)
{
    /// <summary>
    /// A removal in <paramref name="state"/> with <paramref name="reason"/>, made by the harness's owner at
    /// <paramref name="createdUtc"/> (the factory's now when not given), of the copy whose video is the first of
    /// <paramref name="names"/>, holding them in their order; a library notice owed since
    /// <paramref name="libraryNoticeDueUtc"/> when given.
    /// </summary>
    public async Task<PlantedRemoval> PlantAsync(
        CopyRemovalState state,
        IReadOnlyList<PlantedName> names,
        string? reason = null,
        DateTime? createdUtc = null,
        string title = "Alien (1979)",
        DateTime? libraryNoticeDueUtc = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        var video = names.FirstOrDefault(name => name.Kind == ReplacementNameKind.Video)
            ?? throw new ArgumentException("A removal holds its copy's video.", nameof(names));
        var db = factory.Scope.GetRequiredService<ApplicationDbContext>();
        var now = factory.Time.GetUtcNow().UtcDateTime;
        var removals = new CopyRemovalDataFactory(db, factory.Time);
        var removal = await removals.Create(
            harness.Owner.User.Id,
            state,
            video.AppPath,
            ReplacementVerificationHarness.InJellyfin(video.AppPath),
            title,
            reason: reason,
            createdUtc: createdUtc,
            libraryNoticeDueUtc: libraryNoticeDueUtc);

        var rows = new List<CopyRemovalNameEntity>();
        foreach (var (name, position) in names.Select((name, position) => (name, position)))
        {
            var quarantinePath = QuarantinePathOf(removal.Id, name.AppPath);
            rows.Add(await removals.AddName(
                removal.Id,
                position,
                name.Kind,
                name.AppPath,
                quarantinePath,
                ReplacementVerificationHarness.MediaRoot,
                name.Facts,
                name.State,
                name.Problem,
                movedUtc: name.State is ReplacementNameState.Planned or ReplacementNameState.Stayed or ReplacementNameState.Moving ? null : now,
                restoredUtc: name.State == ReplacementNameState.Restored ? now : null,
                deletedUtc: name.State == ReplacementNameState.Deleted ? now : null));

            // The old folder stays whatever moved out of it (ADR 0030: never a folder).
            factory.Files.AddFolder(name.AppPath[..name.AppPath.LastIndexOf('/')]);
            switch (name.State)
            {
                case ReplacementNameState.Quarantined or ReplacementNameState.Restoring or ReplacementNameState.Deleting:
                    factory.Files.AddFile(quarantinePath, name.Facts);
                    break;
                case ReplacementNameState.Planned or ReplacementNameState.Moving or ReplacementNameState.Stayed or ReplacementNameState.Restored:
                    factory.Files.AddFile(name.AppPath, name.Facts);
                    break;
            }
        }

        return new PlantedRemoval(removal, rows);
    }

    /// <summary>
    /// Where the copy at <paramref name="appPath"/>, the app's, waits in the quarantine while the removal
    /// <paramref name="copyRemovalId"/> holds it: <see cref="ReplacementVerificationHarness.QuarantineRoot"/>, the removal's
    /// id, then the copy's path inside <see cref="ReplacementVerificationHarness.MediaRoot"/> (ADR 0030) — the layout a
    /// replacement's names take, under the removal's own id. Written out, not asked of the app.
    /// </summary>
    public static string QuarantinePathOf(Guid copyRemovalId, string appPath) => ReplacementVerificationHarness.QuarantinePathOf(copyRemovalId, appPath);

    /// <summary>Every removal there is, as stored now, oldest first.</summary>
    public async Task<IReadOnlyList<CopyRemovalEntity>> RemovalsAsync(CancellationToken cancellationToken = default)
    {
        List<CopyRemovalEntity> rows = [];
        await factory.WithDbAsync(async db => rows = await db.CopyRemovals.AsNoTracking()
            .OrderBy(removal => removal.CreatedUtc)
            .ThenBy(removal => removal.Id)
            .ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>The names of the removal <paramref name="copyRemovalId"/>, as stored now, in their plan's order.</summary>
    public async Task<IReadOnlyList<CopyRemovalNameEntity>> NamesAsync(Guid copyRemovalId, CancellationToken cancellationToken = default)
    {
        List<CopyRemovalNameEntity> rows = [];
        await factory.WithDbAsync(async db => rows = await db.CopyRemovalNames.AsNoTracking()
            .Where(name => name.CopyRemovalId == copyRemovalId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken));
        return rows;
    }

    /// <summary>
    /// Every audit row aimed at a removal or one of its names, whatever its action, in the order they were saved (SQLite's
    /// row order: the clock stands still in a test, and the ids are random), each with its actor and target. EF writes the
    /// rows of one save in the order of their ids, so the rows one save holds come in any order among themselves.
    /// </summary>
    public async Task<IReadOnlyList<(AuditAction Action, Guid? ActorUserId, AuditTargetKind? TargetKind, Guid? TargetId, string? TargetName)>> AuditsAsync(
        CancellationToken cancellationToken = default)
    {
        AuditTargetKind[] kinds = [AuditTargetKind.CopyRemoval, AuditTargetKind.CopyRemovalName];
        List<(AuditAction, Guid?, AuditTargetKind?, Guid?, string?)> rows = [];
        // Nothing composed onto the raw query: EF would wrap it in a subquery, whose order SQLite needn't keep.
        await factory.WithDbAsync(async db => rows = (await db.AuditEvents
                .FromSqlRaw("SELECT * FROM \"AuditEvents\" ORDER BY rowid")
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(audit => audit.TargetKind is { } kind && kinds.Contains(kind))
            .Select(audit => (audit.Action, audit.ActorUserId, audit.TargetKind, audit.TargetId, audit.TargetName))
            .ToList());
        return rows;
    }
}
