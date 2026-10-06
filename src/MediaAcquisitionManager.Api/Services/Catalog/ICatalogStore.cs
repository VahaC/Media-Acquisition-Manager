using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>
/// MAM-33, ADR 0025 — the one way the metadata catalog's answers reach the database: a read-through cache of the
/// catalog's works, collections, people, genres and lookups, kept in tables of its own and never in the
/// library's. Every list a call returns is stored too, each work's summary as a row, so a later read of one
/// of them needs no call.
/// </summary>
/// <remarks>
/// <para>
/// A cached answer is served while younger than <see cref="CatalogStore.RefreshAge"/> — a work's details by
/// when they were read, everything else by when the catalog last reported it — and read again from the
/// catalog after that. The answers come back in the catalog contract's own records, rebuilt from the rows,
/// so a caller can't tell a cached answer from a fresh one.
/// </para>
/// <para>
/// Expected failures are the catalog's results, passed on as they came (<see cref="IntegrationCallStatus.NotFound"/>
/// for a work, collection or person the catalog doesn't have; the sentence already masked); nothing is
/// stored for a failure. Each call saves what it wrote before it returns and leaves nothing tracked.
/// </para>
/// </remarks>
public interface ICatalogStore
{
    /// <summary>Every genre the catalog files works of <paramref name="kind"/> under, with its role, in the
    /// catalog's id order.</summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogGenre>>> GetGenresAsync(
        CatalogConnection catalog, MediaWorkKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// A film's or a show's details — its genres, IMDb id, collection, directors or creators, seasons, the
    /// works recommended beside it (each a stored summary) and, MAM-34, its runtime, overview and alternative
    /// titles — as of at most <see cref="CatalogStore.RefreshAge"/> ago, and stored by today's reading of the details
    /// (<see cref="CatalogStore.CurrentDetailsVersion"/>): details stored before it are read again.
    /// </summary>
    Task<IntegrationCallResult<CatalogWork>> GetWorkAsync(
        CatalogConnection catalog, MediaWorkKind kind, string catalogId, CancellationToken cancellationToken = default);

    /// <summary>A collection and its films, those still to come included, in the catalog's order. The catalog's
    /// name for it, or <paramref name="collection"/>'s when it gives none.</summary>
    Task<IntegrationCallResult<CatalogCollection>> GetCollectionAsync(
        CatalogConnection catalog, CatalogReference collection, CancellationToken cancellationToken = default);

    /// <summary>A person with the films they directed and the shows they created, in the catalog's order. The
    /// catalog's name for them, or <paramref name="person"/>'s — a work's details name them — when it gives
    /// none.</summary>
    Task<IntegrationCallResult<CatalogPerson>> GetPersonAsync(
        CatalogConnection catalog, CatalogReference person, CancellationToken cancellationToken = default);

    /// <summary>
    /// The one catalog work <paramref name="query"/> most likely names: by an IMDb id, the one work of its kind
    /// the catalog files there; by a title, the one work whose title and year matched
    /// (<see cref="MatchEvidence.TitleAndYear"/>); MAM-33 — by a title up to a year
    /// (<see cref="CatalogLookupQuery.ByTitleUpTo"/>), the one work of the title begun by then, reported as
    /// <see cref="MatchEvidence.TitleAndYear"/> too. None, or two it can't choose between, is
    /// <see cref="CatalogLookupResult.Nothing"/> — cached like a work found.
    /// </summary>
    Task<IntegrationCallResult<CatalogLookupResult>> LookupAsync(
        CatalogConnection catalog, CatalogLookupQuery query, CancellationToken cancellationToken = default);

    /// <summary>Caches <paramref name="query"/>'s answer as nothing usable: the work it found turned out not to
    /// exist (its details answered NotFound), so the next run doesn't look it up again.</summary>
    Task ForgetLookupAsync(CatalogConnection catalog, CatalogLookupQuery query, CancellationToken cancellationToken = default);

    /// <summary>One page (from 1) of the best-rated works of <paramref name="kind"/> with every genre of
    /// <paramref name="genreIds"/> and none of <paramref name="excludedGenreIds"/>, each stored as a summary.
    /// Never cached itself: the page is asked only when a tab is short of seeds.</summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> DiscoverAsync(
        CatalogConnection catalog,
        MediaWorkKind kind,
        IReadOnlyList<int> genreIds,
        IReadOnlyList<int> excludedGenreIds,
        int page,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-34 — the catalog's first page of works of <paramref name="kind"/> titled <paramref name="title"/> (of
    /// <paramref name="year"/> when one is given), in the catalog's order, each stored as a summary: a suggestion
    /// or a search hit shown to the owner is a row, so its poster is served and its facts are read from the rows.
    /// Never cached itself: the suggestions keep what they searched in their own rows, and the owner's own search
    /// wants the catalog's answer of now. A blank title or an episode throws, as the catalog's search does.
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> SearchAsync(
        CatalogConnection catalog, MediaWorkKind kind, string title, int? year, CancellationToken cancellationToken = default);

    /// <summary>
    /// MAM-34 — the films and shows the catalog files under <paramref name="imdbId"/>, the films first, each stored as a
    /// summary like <see cref="SearchAsync"/>'s; an episode's id finds none. Never cached itself: it answers an IMDb id
    /// the owner pasted. A malformed id throws, as the catalog's find does.
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> FindByImdbIdAsync(
        CatalogConnection catalog, string imdbId, CancellationToken cancellationToken = default);

    /// <summary>Stores what a list said of <paramref name="works"/>: each one's summary and genres, a new row or
    /// an update of its own; its details, if read, are left as they were.</summary>
    Task SaveSummariesAsync(CatalogConnection catalog, IReadOnlyList<CatalogWorkSummary> works, CancellationToken cancellationToken = default);
}
