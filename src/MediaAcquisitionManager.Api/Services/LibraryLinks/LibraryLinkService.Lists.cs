using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Paging;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

// MAM-34 — the TMDb page's lists of links and the results of an "Accept all confident" click: filtered, paged and
// ordered by the server (house rule 7), however many links there are.
public sealed partial class LibraryLinkService
{
    private const string FilterMissingError = "Choose which links to list: Unattached, Pending, Conflict, NotWritten or Lost.";

    /// <summary>The longest part of a filter echoed back: the longest filter is 10 characters.</summary>
    private const int FilterEchoLength = 32;

    private static string FilterInvalidError(string filter) =>
        $"“{(filter.Length <= FilterEchoLength ? filter : string.Concat(filter.AsSpan(0, FilterEchoLength - 1), "…"))}” is not a link filter. Use Unattached, Pending, Conflict, NotWritten or Lost.";

    public async Task<LibraryLinkListResult> ListAsync(string? filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return LibraryLinkListResult.Invalid(pagingError);
        var text = filter?.Trim();
        if (string.IsNullOrEmpty(text))
            return LibraryLinkListResult.Invalid(FilterMissingError);

        IQueryable<LibraryItemLinkEntity>? links = text.ToUpperInvariant() switch
        {
            "UNATTACHED" => db.LibraryItemLinks.Where(link => link.MediaWorkId == null),
            "PENDING" => db.LibraryItemLinks.Where(link => link.WriteState == LibraryLinkWriteState.Queued || link.WriteState == LibraryLinkWriteState.Writing),
            "CONFLICT" => Conflicting(),
            // Attached links only: an unattached one's item is gone, so it has no card to open and nothing to retry (Retry
            // needs a work, the repair never takes it), and the Unattached list already shows it.
            "NOTWRITTEN" => db.LibraryItemLinks.Where(link => link.MediaWorkId != null && link.WriteState == LibraryLinkWriteState.NotWritten),
            "LOST" => db.LibraryItemLinks.Where(link => link.WriteState == LibraryLinkWriteState.Lost),
            _ => null,
        };
        if (links is null)
            return LibraryLinkListResult.Invalid(FilterInvalidError(text));

        var totalCount = await links.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        // Past the last page: an empty page that keeps the total, as every list of the app.
        List<LibraryItemLinkEntity> pageLinks = offset >= totalCount
            ? []
            : await links
                .AsNoTracking()
                .OrderByDescending(link => link.SetUtc)
                .ThenBy(link => link.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
        return LibraryLinkListResult.Listed(new LibraryLinkListResponse(await RowsAsync(pageLinks, cancellationToken), totalCount, page, pageSize));
    }

    public async Task<LibraryLinkBatchResponse?> ReadBatchAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var links = await db.LibraryItemLinks
            .AsNoTracking()
            .Where(link => link.BatchId == batchId)
            .OrderBy(link => link.SourceItemId)
            .ToListAsync(cancellationToken);
        if (links.Count == 0)
            return null;

        // A link whose item a sync found gone since is on the Unattached list, not here: it no longer names a work.
        var rows = await RowsAsync(links, cancellationToken);
        return new LibraryLinkBatchResponse(
            batchId,
            [
                .. rows
                    .Where(row => row.WorkId is not null)
                    .GroupBy(row => row.WorkId!.Value)
                    .OrderBy(work => work.First().WorkTitle ?? "", LibraryTitleOrder.Comparer)
                    .ThenBy(work => work.Key)
                    .Select(work => new LibraryLinkBatchItemDto(work.Key, LinkBatchOutcome.Linked, null, [.. work])),
            ]);
    }

    /// <summary>
    /// The links whose item's library holds a TMDb id of the link's number space that is neither the link's nor — while
    /// the link isn't confirmed (R6) — the one it replaces: neither wins. For an episode-to-show link, a film id other than
    /// the one it replaced, whatever its state (R7) — one the library gave the item after it was set.
    /// <c>LinkedIdentifiers.ConflictingValue</c>'s rule spelled out as SQL, as the lists page and count in the database.
    /// </summary>
    private IQueryable<LibraryItemLinkEntity> Conflicting() =>
        db.LibraryItemLinks.Where(link => link.MediaWorkId != null
            && db.MediaWorkIdentifiers.Any(row => row.SourceKind == ProvenanceSourceKind.MediaLibrary
                && row.SourceConnectionId == link.IntegrationConnectionId
                && row.SourceItemId == link.SourceItemId
                && ((link.Target == LibraryLinkTarget.Work
                        && row.Scheme == link.Scheme
                        && row.Value.Trim().ToUpper() != link.Value.Trim().ToUpper())
                    || (link.Target == LibraryLinkTarget.ShowEpisode && row.Scheme == MediaIdentifierScheme.Tmdb))
                && !(link.SupersededValue != null
                    && (link.Target == LibraryLinkTarget.ShowEpisode
                        || link.WriteState == LibraryLinkWriteState.KeptInApp
                        || link.WriteState == LibraryLinkWriteState.Queued
                        || link.WriteState == LibraryLinkWriteState.Writing
                        || link.WriteState == LibraryLinkWriteState.NotWritten)
                    && row.Value.Trim().ToUpper() == link.SupersededValue.Trim().ToUpper())));

    /// <summary>
    /// <paramref name="links"/> as the lists show them, in their order: each with its work's title, its library's
    /// name and the conflict its item's library ids make. Batched: one query each for the titles, the libraries and the
    /// items' ids, however many links.
    /// </summary>
    private async Task<IReadOnlyList<LibraryLinkRowDto>> RowsAsync(IReadOnlyList<LibraryItemLinkEntity> links, CancellationToken cancellationToken)
    {
        if (links.Count == 0)
            return [];

        List<Guid> workIds = [.. links.Where(link => link.MediaWorkId is not null).Select(link => link.MediaWorkId!.Value).Distinct()];
        var titles = (await db.MediaWorkTitles.AsNoTracking().Where(title => workIds.Contains(title.MediaWorkId)).ToListAsync(cancellationToken))
            .ToLookup(title => title.MediaWorkId);
        var connections = await ConnectionsAsync([.. links.Select(link => link.IntegrationConnectionId).Distinct()], cancellationToken);
        List<Guid?> connectionIds = [.. connections.Keys.Select(id => (Guid?)id)];
        List<string?> itemIds = [.. links.Select(link => (string?)link.SourceItemId).Distinct()];
        var libraryRows = (await db.MediaWorkIdentifiers
                .AsNoTracking()
                .Where(row => row.SourceKind == ProvenanceSourceKind.MediaLibrary
                    && connectionIds.Contains(row.SourceConnectionId)
                    && itemIds.Contains(row.SourceItemId))
                .ToListAsync(cancellationToken))
            .ToLookup(row => (row.SourceConnectionId!.Value, row.SourceItemId!));

        return
        [
            .. links.Select(link => linkMapper.ToRowDto(
                link,
                link.MediaWorkId is { } workId ? TitleOf([.. titles[workId]]) : null,
                connections.TryGetValue(link.IntegrationConnectionId, out var connection) ? connection.Name : "",
                [.. libraryRows[(link.IntegrationConnectionId, link.SourceItemId)]])),
        ];
    }
}
