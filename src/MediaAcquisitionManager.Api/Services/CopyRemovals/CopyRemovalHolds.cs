using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the files another write to the media already holds (rule 7): each copy an open replacement ticked and didn't
/// drop, and each name of an open removal — by path and by identity, as a replacement's AlreadyScheduled reads them
/// (R3), so a second name of one file is held as the first.
/// </summary>
/// <param name="ReplacementPaths">The paths open replacements hold.</param>
/// <param name="ReplacementIdentities">The files open replacements hold, by device and inode.</param>
/// <param name="RemovalPaths">The paths open removals hold.</param>
/// <param name="RemovalIdentities">The files open removals hold, by device and inode.</param>
public sealed record CopyRemovalHolds(
    IReadOnlyList<NormalizedPath> ReplacementPaths,
    IReadOnlySet<(long DeviceId, long Inode)> ReplacementIdentities,
    IReadOnlyList<NormalizedPath> RemovalPaths,
    IReadOnlySet<(long DeviceId, long Inode)> RemovalIdentities)
{
    /// <summary>Whether an open replacement holds the file at <paramref name="appPath"/> of <paramref name="deviceId"/> and
    /// <paramref name="inode"/>.</summary>
    public bool ReplacementHolds(NormalizedPath appPath, long? deviceId, long? inode) =>
        ReplacementPaths.Any(held => held.IsSameAs(appPath)) || (deviceId is { } device && inode is { } node && ReplacementIdentities.Contains((device, node)));

    /// <summary>Whether an open removal holds the file at <paramref name="appPath"/> of <paramref name="deviceId"/> and
    /// <paramref name="inode"/>.</summary>
    public bool RemovalHolds(NormalizedPath appPath, long? deviceId, long? inode) =>
        RemovalPaths.Any(held => held.IsSameAs(appPath)) || (deviceId is { } device && inode is { } node && RemovalIdentities.Contains((device, node)));

    /// <summary>What every open replacement and open removal holds now: two queries of the few rows still going.</summary>
    public static async Task<CopyRemovalHolds> ReadAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        // MAM-27 — a copy the owner dropped from its job is no longer held by it, as the Download dialog reads it.
        var replacements = await (
                from file in db.ReplacementJobFiles.AsNoTracking()
                join job in db.ReplacementJobs.AsNoTracking() on file.ReplacementJobId equals job.Id
                where EF.Constant(ReplacementJobStates.Open).Contains(job.State) && file.DroppedUtc == null
                select new { file.AppPath, file.DeviceId, file.Inode })
            .ToListAsync(cancellationToken);
        var removals = await RemovalNamesAsync(db, cancellationToken);
        return new CopyRemovalHolds(
            [.. replacements.Select(file => NormalizedPath.TryParse(file.AppPath, out var path, out _) ? path : null).OfType<NormalizedPath>()],
            replacements.Select(file => (file.DeviceId, file.Inode)).ToHashSet(),
            [.. removals.Select(name => NormalizedPath.TryParse(name.AppPath, out var path, out _) ? path : null).OfType<NormalizedPath>()],
            removals.Select(name => (name.DeviceId, name.Inode)).ToHashSet());
    }

    /// <summary>
    /// The names of every open removal (<see cref="CopyRemovalStates.Open"/>), by path and identity: what a replacement's
    /// Download dialog and send read as already scheduled, as a removal refuses a copy an open replacement holds.
    /// </summary>
    public static async Task<IReadOnlyList<(string AppPath, long DeviceId, long Inode)>> RemovalNamesAsync(
        ApplicationDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return (await (
                    from name in db.CopyRemovalNames.AsNoTracking()
                    join removal in db.CopyRemovals.AsNoTracking() on name.CopyRemovalId equals removal.Id
                    where EF.Constant(CopyRemovalStates.Open).Contains(removal.State)
                    select new { name.AppPath, name.DeviceId, name.Inode })
                .ToListAsync(cancellationToken))
            .Select(name => (name.AppPath, name.DeviceId, name.Inode))
            .ToList();
    }
}
