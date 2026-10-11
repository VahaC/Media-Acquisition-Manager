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
    public async Task<PlantedReplacement> PlantAsync(
        ReplacementJobState state,
        IReadOnlyList<PlantedName> names,
        DateTime? quarantineCheckedUtc = null,
        string? reason = null,
        Guid? requestedByUserId = null)
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
            quarantinedUtc: state == ReplacementJobState.Quarantined ? now : null);

        var copies = new List<ReplacementJobFileEntity>();
        var rows = new List<ReplacementJobNameEntity>();
        foreach (var (name, position) in names.Select((name, position) => (name, position)))
        {
            if (name.Kind == ReplacementNameKind.Video)
            {
                copies.Add(await replacements.AddFile(
                    replacement.Id,
                    name.AppPath,
                    name.Facts,
                    position: copies.Count,
                    servicePath: ReplacementVerificationHarness.InJellyfin(name.AppPath)));
            }

            var copy = copies.LastOrDefault() ?? throw new ArgumentException("A companion follows the video it belongs to.", nameof(names));
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

        return new PlantedReplacement(download, replacement, copies, rows);
    }
}
