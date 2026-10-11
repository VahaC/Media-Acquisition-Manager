using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-26 — a replacement as the mover left it (ADR 0030), planted directly in the database and on the fake disk over
/// <see cref="ReplacementVerificationHarness"/>'s world, for the HTTP tests of the owner's own actions: its download done,
/// its ticked copies, its names in the states given, and each file where its state says it is — in the quarantine at
/// <see cref="ReplacementVerificationHarness.QuarantinePathOf"/>, or at its old path —, every old folder still there.
/// MAM-27 — or as the check of its new files left it: copies with no name, each with its pair, problem or drop, and the
/// new files with the evidence the check gathered.
/// </summary>
/// <remarks>
/// The mover itself is tested by moving (<c>QuarantineReplacementsAsync</c>); what the owner does next stands on rows a
/// move would have written, so a test of Restore or Delete for good doesn't hang on the mover's every rule.
/// </remarks>
public sealed class ReplacementQuarantinePlanter(MediaAcquisitionManagerApiFactory factory, ReplacementVerificationHarness harness)
{
    /// <summary>The release whose download brings the new copy, which the job's audit rows are named by.</summary>
    public const string ReleaseTitle = "Alien (1979) Director's Cut BDRemux 2160p HDR";

    /// <summary>
    /// A replacement in <paramref name="state"/> with <paramref name="reason"/>, scheduled by the harness's owner — or by
    /// <paramref name="requestedByUserId"/> —, the mover's stage marked at <paramref name="quarantineCheckedUtc"/>, holding
    /// <paramref name="names"/> in their order: each video starts a ticked copy, its companions follow it. A Quarantined job
    /// reached the quarantine at the factory's now; no library notice is owed.
    /// </summary>
    /// <remarks>
    /// MAM-27 — the check started at <paramref name="inspectionStartedUtc"/> and asked the library at
    /// <paramref name="libraryAskedUtc"/> when given; <paramref name="newFiles"/> are planted first, in the client's order
    /// — each on the disk at its path, when it has one and the disk's identity —; then the copies of
    /// <paramref name="names"/>, then <paramref name="copies"/>, which have no name and stay at their old paths, each paired
    /// with the new file at its <see cref="PlantedCopy.PairedWithNewFile"/>.
    /// </remarks>
    public async Task<PlantedReplacement> PlantAsync(
        ReplacementJobState state,
        IReadOnlyList<PlantedName> names,
        DateTime? quarantineCheckedUtc = null,
        string? reason = null,
        Guid? requestedByUserId = null,
        IReadOnlyList<PlantedCopy>? copies = null,
        IReadOnlyList<ReplacementNewFileFacts>? newFiles = null,
        DateTime? inspectionStartedUtc = null,
        DateTime? libraryAskedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        var db = factory.Scope.GetRequiredService<ApplicationDbContext>();
        var now = factory.Time.GetUtcNow().UtcDateTime;
        var requestedBy = requestedByUserId ?? harness.Owner.User.Id;

        var release = await new ReleaseSeeder(factory.Scope).Create(harness.Toloka, ReleaseTitle);
        var download = await new DownloadJobDataFactory(db, factory.Time).Create(
            release.Id,
            harness.Client.Id,
            harness.Client.Name,
            harness.Movies.Id,
            DownloadJobStatus.Downloaded,
            requestedBy,
            overwriteCheck: DownloadOverwriteCheck.Checked);
        var replacements = new ReplacementJobDataFactory(db, factory.Time);
        var replacement = await replacements.Create(
            download.Id,
            requestedBy,
            state,
            reason,
            quarantineCheckedUtc: quarantineCheckedUtc,
            quarantinedUtc: state == ReplacementJobState.Quarantined ? now : null,
            inspectionStartedUtc: inspectionStartedUtc,
            libraryAskedUtc: libraryAskedUtc);

        var newFileRows = new List<ReplacementJobNewFileEntity>();
        foreach (var (facts, position) in (newFiles ?? []).Select((facts, position) => (facts, position)))
        {
            newFileRows.Add(await replacements.AddNewFile(replacement.Id, position, facts));
            if (facts is { AppPath: { } newPath, Disk: { } disk })
            {
                factory.Files.AddFolder(newPath[..newPath.LastIndexOf('/')]);
                factory.Files.AddFile(newPath, disk);
            }
        }

        var ticked = new List<ReplacementJobFileEntity>();
        var rows = new List<ReplacementJobNameEntity>();
        foreach (var (name, position) in names.Select((name, position) => (name, position)))
        {
            if (name.Kind == ReplacementNameKind.Video)
            {
                ticked.Add(await replacements.AddFile(
                    replacement.Id,
                    name.AppPath,
                    name.Facts,
                    position: ticked.Count,
                    servicePath: ReplacementVerificationHarness.InJellyfin(name.AppPath)));
            }

            var copy = ticked.LastOrDefault() ?? throw new ArgumentException("A companion follows the video it belongs to.", nameof(names));
            var quarantinePath = ReplacementVerificationHarness.QuarantinePathOf(replacement.Id, name.AppPath);
            rows.Add(await replacements.AddName(
                replacement.Id,
                copy.Id,
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

        foreach (var copy in copies ?? [])
        {
            ticked.Add(await replacements.AddFile(
                replacement.Id,
                copy.AppPath,
                copy.Facts,
                position: ticked.Count,
                servicePath: ReplacementVerificationHarness.InJellyfin(copy.AppPath),
                pairedNewFileId: copy.PairedWithNewFile is { } pair ? newFileRows[pair].Id : null,
                problem: copy.Problem,
                onlyDropSettles: copy.OnlyDropSettles,
                droppedUtc: copy.DroppedUtc,
                seasonNumber: copy.SeasonNumber,
                episodeNumber: copy.EpisodeNumber));
            // Never moved: it is where it was scheduled, dropped or not.
            factory.Files.AddFolder(copy.AppPath[..copy.AppPath.LastIndexOf('/')]);
            factory.Files.AddFile(copy.AppPath, copy.Facts);
        }

        return new PlantedReplacement(download, replacement, ticked, rows, newFileRows);
    }
}
