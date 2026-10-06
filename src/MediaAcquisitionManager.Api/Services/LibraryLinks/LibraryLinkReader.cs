using System.Linq.Expressions;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkReader"/>
/// <remarks>
/// <para>
/// In memory, each linked item's library rows go through <see cref="LinkedIdentifiers"/> itself. The SQL methods can't
/// call it, so they spell its rule out once each — the id a link replaces gives way while the link isn't confirmed, the
/// link's TMDb id counts, TMDb's IMDb id counts where the item's library holds none — and <c>LibraryWorksLinksHttpTests</c>
/// asks a card, the search and the missing-ids filter the same questions, so the two can't drift apart.
/// </para>
/// <para>
/// An item's library rows are the work's rows the item's library reported in it (<c>SourceKind == MediaLibrary</c>, the
/// link's integration and item id): a sync puts them on the work the link sits on.
/// </para>
/// </remarks>
public sealed class LibraryLinkReader(ApplicationDbContext db) : ILibraryLinkReader
{
    public async Task<IReadOnlyDictionary<Guid, LinkedWork>> ReadAsync(
        IReadOnlyCollection<Guid> workIds, bool forOwner, CancellationToken cancellationToken = default)
    {
        var ids = workIds.Distinct().ToList();
        // The ids as one JSON parameter (EF.Parameter, the LibrarySnapshotStore rule): the recommendations read every
        // work of the library at once, which would run into SQLite's limit on the number of parameters.
        var rows = (await db.MediaWorkIdentifiers.AsNoTracking()
                .Where(identifier => EF.Parameter(ids).Contains(identifier.MediaWorkId))
                .ToListAsync(cancellationToken))
            .ToLookup(identifier => identifier.MediaWorkId);
        // R16 — an episode counts as linked when its series is: the series of the works asked for that are episodes.
        var seriesOf = (await db.MediaEpisodePositions.AsNoTracking()
                .Where(position => position.SeriesWorkId != null && EF.Parameter(ids).Contains(position.MediaWorkId))
                .Select(position => new { position.MediaWorkId, SeriesWorkId = position.SeriesWorkId!.Value })
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToLookup(position => position.MediaWorkId, position => position.SeriesWorkId);
        var linkedIds = ids.Concat(seriesOf.SelectMany(series => series)).Distinct().ToList();
        var links = (await db.LibraryItemLinks.AsNoTracking()
                .Where(link => link.MediaWorkId != null && EF.Parameter(linkedIds).Contains(link.MediaWorkId!.Value))
                .ToListAsync(cancellationToken))
            .ToLookup(link => link.MediaWorkId!.Value);

        return ids.ToDictionary(
            id => id,
            id => Read([.. rows[id]], [.. links[id]], seriesOf[id].Any(series => links[series].Any()), forOwner));
    }

    public IQueryable<Guid> WorksHolding(MediaIdentifierScheme scheme, string key) =>
        Hits(
            row => row.Scheme == scheme && row.Value.Trim().ToUpper() == key,
            link => link.Scheme == scheme && link.Value.Trim().ToUpper() == key,
            scheme == MediaIdentifierScheme.Imdb ? link => link.ImdbId!.Trim().ToUpper() == key : null);

    public IQueryable<Guid> WorksWithAnIdContaining(string text) =>
        Hits(
            row => row.Value.ToUpper().Contains(text),
            link => link.Value.ToUpper().Contains(text),
            link => link.ImdbId!.ToUpper().Contains(text));

    public IQueryable<Guid> LinkedWorkIds()
    {
        var linked = db.LibraryItemLinks.Where(link => link.MediaWorkId != null).Select(link => link.MediaWorkId!.Value);
        return linked.Union(db.MediaEpisodePositions
            .Where(position => position.SeriesWorkId != null && linked.Contains(position.SeriesWorkId!.Value))
            .Select(position => position.MediaWorkId));
    }

    // ── in memory ────────────────────────────────────────────────────────────────────────────────

    /// <summary>One work's rows with its links applied, item by item.</summary>
    private static LinkedWork Read(
        IReadOnlyList<MediaWorkIdentifierEntity> rows, IReadOnlyList<LibraryItemLinkEntity> links, bool seriesIsLinked, bool forOwner)
    {
        if (links.Count == 0)
            return new LinkedWork(rows, [], null, seriesIsLinked);

        // The latest set first: the card's link line names it, and of two items' conflicts the first is shown.
        var ordered = links.OrderByDescending(link => link.SetUtc).ThenBy(link => link.Id).ToList();
        // The rows of no linked item — another item's, another source's — stay as they are.
        var identifiers = rows.Where(row => !ordered.Any(link => IsItemRow(row, link))).ToList();
        string? conflictingValue = null;
        foreach (var link in ordered)
        {
            var itemRows = rows.Where(row => IsItemRow(row, link)).ToList();
            List<MediaIdentifier> libraryIdentifiers = [.. itemRows.Select(row => new MediaIdentifier(row.Scheme, row.Value))];
            var facts = new ItemLinkFacts(link.Target, link.Scheme, link.Value, link.SupersededValue, link.ImdbId, link.WriteState);
            var effective = LinkedIdentifiers.Effective(libraryIdentifiers, facts);

            // A library row stays while the rule keeps its id: the one a link replaces gives way.
            identifiers.AddRange(itemRows.Where(row => effective.Contains(new MediaIdentifier(row.Scheme, row.Value))));
            if (link.Target == LibraryLinkTarget.Work)
            {
                identifiers.Add(PersonRow(link, forOwner));
                // The rule adds TMDb's IMDb id only where the item's library holds none (R8).
                if (effective.FirstOrDefault(identifier => identifier.Scheme == MediaIdentifierScheme.Imdb && !libraryIdentifiers.Contains(identifier)) is { } imdb)
                    identifiers.Add(CatalogRow(link, imdb.Value));
            }

            conflictingValue ??= LinkedIdentifiers.ConflictingValue(libraryIdentifiers, facts);
        }

        return new LinkedWork(identifiers, ordered, conflictingValue, IsLinked: true);
    }

    private static bool IsItemRow(MediaWorkIdentifierEntity row, LibraryItemLinkEntity link) =>
        row.SourceKind == ProvenanceSourceKind.MediaLibrary
        && row.SourceConnectionId == link.IntegrationConnectionId
        && string.Equals(row.SourceItemId, link.SourceItemId, StringComparison.Ordinal);

    // The link's TMDb id as a row whose source is the person who set it. Made up for reading, never tracked or saved: a
    // link is never a fact on the work (ADR 0008). Its id is the link's, so the readers' orders stay the same each time.
    private static MediaWorkIdentifierEntity PersonRow(LibraryItemLinkEntity link, bool forOwner) =>
        new()
        {
            Id = link.Id,
            MediaWorkId = link.MediaWorkId!.Value,
            Scheme = link.Scheme,
            Value = link.Value,
            SourceKind = ProvenanceSourceKind.Person,
            // The owner's sign-in name: the owner's only (the sync run's rule); everyone else reads "set by hand".
            SourceName = forOwner ? link.SetByUserName : null,
            SourceItemId = link.SourceItemId,
            ObservedUtc = link.SetUtc,
            CreatedUtc = link.SetUtc,
        };

    // The IMDb id TMDb gave for the linked film or show, as a row whose source is the catalog; read-only like the
    // person's row, under the link's id too.
    private static MediaWorkIdentifierEntity CatalogRow(LibraryItemLinkEntity link, string imdbId) =>
        new()
        {
            Id = link.Id,
            MediaWorkId = link.MediaWorkId!.Value,
            Scheme = MediaIdentifierScheme.Imdb,
            Value = imdbId,
            SourceKind = ProvenanceSourceKind.Metadata,
            SourceConnectionId = link.ImdbSourceConnectionId,
            SourceName = link.ImdbSourceName,
            SourceItemId = link.Value,
            ObservedUtc = link.ImdbObservedUtc ?? link.SetUtc,
            CreatedUtc = link.ImdbObservedUtc ?? link.SetUtc,
        };

    // ── SQL ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The works with a counted id passing the tests: a library row that doesn't give way passing
    /// <paramref name="libraryRow"/>; an attached link to the work itself whose TMDb id passes <paramref name="linkValue"/>;
    /// and — when <paramref name="linkImdb"/> is given — one whose IMDb id from TMDb passes it, where the item's library
    /// holds no IMDb id.
    /// </summary>
    private IQueryable<Guid> Hits(
        Expression<Func<MediaWorkIdentifierEntity, bool>> libraryRow,
        Expression<Func<LibraryItemLinkEntity, bool>> linkValue,
        Expression<Func<LibraryItemLinkEntity, bool>>? linkImdb)
    {
        var hits = CountedLibraryRows().Where(libraryRow).Select(row => row.MediaWorkId)
            .Union(LinksToTheWork().Where(linkValue).Select(link => link.MediaWorkId!.Value));
        return linkImdb is null
            ? hits
            : hits.Union(CountedCatalogImdbIds().Where(linkImdb).Select(link => link.MediaWorkId!.Value));
    }

    /// <summary>
    /// <see cref="LinkedIdentifiers"/>'s first step in SQL: the library rows, less each linked item's row of the id its
    /// link replaces while the link isn't confirmed in the library (kept in the app, queued, being written, not written —
    /// R6) — of the link's scheme for a link to the work, the film id for an episode-to-show link (R7).
    /// </summary>
    private IQueryable<MediaWorkIdentifierEntity> CountedLibraryRows() =>
        db.MediaWorkIdentifiers.Where(row => !db.LibraryItemLinks.Any(link =>
            link.MediaWorkId == row.MediaWorkId
            && row.SourceKind == ProvenanceSourceKind.MediaLibrary
            && row.SourceConnectionId == link.IntegrationConnectionId
            && row.SourceItemId == link.SourceItemId
            && link.SupersededValue != null
            && (link.WriteState == LibraryLinkWriteState.KeptInApp
                || link.WriteState == LibraryLinkWriteState.Queued
                || link.WriteState == LibraryLinkWriteState.Writing
                || link.WriteState == LibraryLinkWriteState.NotWritten)
            && ((link.Target == LibraryLinkTarget.Work && row.Scheme == link.Scheme)
                || (link.Target == LibraryLinkTarget.ShowEpisode && row.Scheme == MediaIdentifierScheme.Tmdb))
            && row.Value.Trim().ToUpper() == link.SupersededValue.Trim().ToUpper()));

    /// <summary>The attached links to the work itself: an episode-to-show link counts none of the show's ids as the
    /// item's.</summary>
    private IQueryable<LibraryItemLinkEntity> LinksToTheWork() =>
        db.LibraryItemLinks.Where(link => link.MediaWorkId != null && link.Target == LibraryLinkTarget.Work);

    /// <summary>R8 — the links to the work whose IMDb id from TMDb counts: those whose item's library holds no IMDb id of
    /// its own.</summary>
    private IQueryable<LibraryItemLinkEntity> CountedCatalogImdbIds() =>
        LinksToTheWork().Where(link =>
            link.ImdbId != null
            && link.ImdbId.Trim() != ""
            && !db.MediaWorkIdentifiers.Any(row =>
                row.MediaWorkId == link.MediaWorkId
                && row.SourceKind == ProvenanceSourceKind.MediaLibrary
                && row.SourceConnectionId == link.IntegrationConnectionId
                && row.SourceItemId == link.SourceItemId
                && row.Scheme == MediaIdentifierScheme.Imdb));
}
