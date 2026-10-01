using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Releases;

public sealed partial class ReleaseSearchService
{
    /// <summary>
    /// How long a found release is kept for sending it to a client (MAM-13) when no job points at it: a
    /// result list stays open for an evening, and the next search lists the release again anyway, under a
    /// new id and with today's seeders. A day of searches is a few thousand rows at most (100 per source
    /// per search, ReleaseSearchPlanning), so the table never needs an index for the prune.
    /// </summary>
    private static readonly TimeSpan UnusedReleaseLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Every hit of every source as a new release row, its download link sealed to the row (ADR 0007), and
    /// each source with the ids of its rows in its hits' order. A source removed while it was being
    /// searched is left out, rows and all.
    /// </summary>
    /// <remarks>
    /// MAM-10 — new rows every search, never an update of an earlier one: a download job points at the
    /// release it sent (Restrict, MAM-7), and that evidence must not change under it when the listing is
    /// found again.
    /// </remarks>
    private async Task<IReadOnlyList<(ReleaseSourceOutcome Outcome, IReadOnlyList<Guid> ReleaseIds)>> StoreAsync(
        IReadOnlyList<ReleaseSourceOutcome> outcomes, CancellationToken cancellationToken)
    {
        if (outcomes.Count == 0)
            return [];

        // One transaction: the names are read again inside it rather than trusted from before the sources
        // were asked (the LibrarySnapshotStore rule). The owner may have removed one since — a row
        // pointing at it would break its foreign key and lose every other source's results with it — or
        // renamed it, and each row carries the current name.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var searchedIds = outcomes.Select(outcome => outcome.IntegrationId).ToList();
        var currentNames = await db.IntegrationConnections
            .AsNoTracking()
            .Where(connection => searchedIds.Contains(connection.Id))
            .ToDictionaryAsync(connection => connection.Id, connection => connection.Name, cancellationToken);

        var now = time.GetUtcNow().UtcDateTime;
        var stored = new List<(ReleaseSourceOutcome Outcome, IReadOnlyList<Guid> ReleaseIds)>();
        foreach (var outcome in outcomes)
        {
            if (!currentNames.TryGetValue(outcome.IntegrationId, out var name))
                continue;

            var releaseIds = new List<Guid>(outcome.Hits.Count);
            foreach (var hit in outcome.Hits)
            {
                var release = new ReleaseEntity
                {
                    SourceKind = ProvenanceSourceKind.ReleaseSearch,
                    SourceConnectionId = outcome.IntegrationId,
                    SourceName = name,
                    SourceItemId = hit.SourceItemId,
                    ObservedUtc = now,
                    CreatedUtc = now,
                    Title = hit.Title,
                    SizeBytes = hit.SizeBytes,
                    PublishedUtc = hit.PublishedUtc,
                    SourcePageUrl = hit.SourcePageUrl,
                    PayloadKind = hit.PayloadKind,
                    InfoHash = hit.InfoHash,
                };
                // Sealed to this row before its first save: the id is assigned on construction.
                release.PayloadLocatorEncrypted = encryption.Encrypt(hit.Locator.Value, ReleasePayloadLocatorPurpose.For(release.Id));
                db.Releases.Add(release);
                releaseIds.Add(release.Id);
            }

            stored.Add((outcome with { Name = name }, releaseIds));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // The rows are written and nothing reads them back through this context.
        db.ChangeTracker.Clear();
        return stored;
    }

    /// <summary>
    /// Removes the releases stored more than <see cref="UnusedReleaseLifetime"/> ago that no download job
    /// points at, in one statement — SQLite's single writer makes the job check and the delete one step,
    /// so a second search pruning at the same moment deletes nothing twice. A failure is logged and never
    /// fails the search that triggered it: the next search prunes again.
    /// </summary>
    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - UnusedReleaseLifetime;
        try
        {
            var removed = await db.Releases
                .Where(release => release.CreatedUtc < cutoff && !db.DownloadJobs.Any(job => job.ReleaseId == release.Id))
                .ExecuteDeleteAsync(cancellationToken);
            if (removed > 0)
                logger.LogInformation("Removed {Count} releases no download job used.", removed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Removing the releases no download job used failed; the next search tries again.");
        }
    }
}
