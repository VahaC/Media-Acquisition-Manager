using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <inheritdoc cref="IReleaseStore"/>
/// <remarks>
/// MAM-12 — moved out of <see cref="ReleaseSearchService"/>, so a stored search keeps its releases the
/// way a plain release search does: the same rows, sealed the same way, pruned by the same rule.
/// </remarks>
public sealed class ReleaseStore(
    ApplicationDbContext db,
    IEncryptionService encryption,
    TimeProvider time,
    ILogger<ReleaseStore> logger) : IReleaseStore
{
    /// <summary>
    /// How long a found release is kept for sending it to a client (MAM-13) when no job points at it: a
    /// result list stays open for an evening, and the next search lists the release again anyway, under a
    /// new id and with today's seeders. A day of searches is a few thousand rows at most (100 per source
    /// per search, ReleaseSearchPlanning), so the table never needs an index for the prune. MAM-12 — a
    /// stored search lives as long as its releases, so it is read as gone once this has passed, pruned
    /// or not yet.
    /// </summary>
    public static readonly TimeSpan UnusedReleaseLifetime = TimeSpan.FromHours(24);

    /// <remarks>
    /// MAM-10 — new rows every search, never an update of an earlier one: a download job points at the
    /// release it sent (Restrict, MAM-7), and that evidence must not change under it when the listing is
    /// found again.
    /// </remarks>
    public async Task<IReadOnlyList<StoredReleaseSource>> StoreAsync(
        IReadOnlyList<ReleaseSourceOutcome> outcomes,
        SearchEntity? search,
        CancellationToken cancellationToken = default)
    {
        if (outcomes.Count == 0 && search is null)
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
        if (search is not null)
        {
            // MAM-12 — the same instant as its releases: the prune removes both by one cutoff, so neither
            // outlives the other by the moments between two clock reads.
            search.CreatedUtc = now;
            db.Searches.Add(search);
        }

        var stored = new List<StoredReleaseSource>();
        foreach (var outcome in outcomes)
        {
            if (!currentNames.TryGetValue(outcome.IntegrationId, out var name))
                continue;

            SearchSourceEntity? source = null;
            if (search is not null)
            {
                // MAM-12 — how the source answered, kept for every read of the search: its place is its
                // place among the stored ones, so the positions run 0, 1, 2… with none missing.
                source = new SearchSourceEntity
                {
                    SearchId = search.Id,
                    IntegrationConnectionId = outcome.IntegrationId,
                    Name = name,
                    Position = stored.Count,
                    Status = outcome.Status,
                    NotSearchedReason = outcome.NotSearchedReason,
                    Error = outcome.Error is null ? null : Cut(outcome.Error),
                };
                db.SearchSources.Add(source);
            }

            var releaseIds = new List<Guid>(outcome.Hits.Count);
            foreach (var (position, hit) in outcome.Hits.Index())
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
                    // MAM-12 — what a stored search's list filters and sorts by, kept for a plain search's
                    // rows too: one row, whichever search found it.
                    SearchSourceId = source?.Id,
                    Position = position,
                    Seeders = hit.Seeders,
                    Peers = hit.Peers,
                    ImdbId = hit.Identifiers.FirstOrDefault(identifier => identifier.Scheme == MediaIdentifierScheme.Imdb)?.Value,
                    TmdbId = hit.Identifiers.FirstOrDefault(identifier => identifier.Scheme == MediaIdentifierScheme.Tmdb)?.Value,
                };
                // Sealed to this row before its first save: the id is assigned on construction.
                release.PayloadLocatorEncrypted = encryption.Encrypt(hit.Locator.Value, ReleasePayloadLocatorPurpose.For(release.Id));
                db.Releases.Add(release);
                releaseIds.Add(release.Id);
            }

            stored.Add(new StoredReleaseSource(outcome with { Name = name }, releaseIds));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // The rows are written and nothing reads them back through this context.
        db.ChangeTracker.Clear();
        return stored;
    }

    /// <remarks>
    /// Each removal is one statement — SQLite's single writer makes the job check and the delete one step,
    /// so a second search pruning at the same moment deletes nothing twice — and each fails on its own: a
    /// release prune that fails still lets the searches go. The releases go first, so the searches' delete
    /// only unlinks the ones a job keeps.
    /// </remarks>
    public async Task PruneAsync(CancellationToken cancellationToken = default)
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

        try
        {
            // MAM-12 — the database itself takes the rest (ApplicationDbContext): the searches' sources go
            // with them (Cascade), and a release a download job keeps loses its link to its source
            // (SetNull) rather than holding the search back.
            var removed = await db.Searches
                .Where(search => search.CreatedUtc < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (removed > 0)
                logger.LogInformation("Removed {Count} searches made more than a day ago.", removed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Removing the searches made more than a day ago failed; the next search tries again.");
        }
    }

    // A sentence past the column is cut with an ellipsis, so the reader sees it was cut. The column's own
    // constant, so the cut and the column can't drift apart (the LibrarySyncRunUpdates rule).
    private static string Cut(string error) =>
        error.Length <= SearchSourceEntity.ErrorMaximumLength
            ? error
            : string.Concat(error.AsSpan(0, SearchSourceEntity.ErrorMaximumLength - 1), "…");
}
