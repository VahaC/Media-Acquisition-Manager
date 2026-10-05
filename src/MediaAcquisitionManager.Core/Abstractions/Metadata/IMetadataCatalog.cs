using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>
/// MAM-33 — the catalog a metadata source keeps of films and shows the library may not have: their
/// details, collections, people, genres and posters, which recommendations are built from. A further
/// interface of an <see cref="IIntegrationAdapter"/> class beside <see cref="IMetadataResolver"/>, as
/// every kind contract is (ADR 0005).
/// </summary>
/// <remarks>
/// <para>
/// A work is a film (<see cref="MediaWorkKind.Movie"/>) or a show (<see cref="MediaWorkKind.Series"/>),
/// each numbered by the source in its own space: the same id can name a film and a show (ADR 0017).
/// Ids are the source's, as text; titles come in the source's catalog language beside the original.
/// </para>
/// <para>
/// All methods return result types and never throw on expected failures (unreachable, refused,
/// unexpected answer, a work the source doesn't know); only a cancellation by the caller propagates, as
/// <see cref="OperationCanceledException"/>. Every message is one readable English sentence with the
/// call's secrets already masked. A caller's mistake — an episode, a malformed id — throws before
/// anything is sent.
/// </para>
/// </remarks>
public interface IMetadataCatalog
{
    /// <summary>
    /// A film's or a show's details in one call: its genres, collection, directors or creators, seasons
    /// and the works the source recommends beside it. <see cref="IntegrationCallStatus.NotFound"/> when the
    /// source knows no such work.
    /// </summary>
    Task<IntegrationCallResult<CatalogWork>> GetWorkAsync(IntegrationConnectionSettings settings, MediaWorkKind kind, string id, CancellationToken cancellationToken = default);

    /// <summary>A collection of films and every part of it. <see cref="IntegrationCallStatus.NotFound"/> when
    /// the source knows no such collection.</summary>
    Task<IntegrationCallResult<CatalogCollection>> GetCollectionAsync(IntegrationConnectionSettings settings, string id, CancellationToken cancellationToken = default);

    /// <summary>A person, with the films they directed and the shows they created.
    /// <see cref="IntegrationCallStatus.NotFound"/> when the source knows no such person.</summary>
    Task<IntegrationCallResult<CatalogPerson>> GetPersonAsync(IntegrationConnectionSettings settings, string id, CancellationToken cancellationToken = default);

    /// <summary>The works of <paramref name="kind"/> titled <paramref name="title"/>, of
    /// <paramref name="year"/> when one is given, as the source's search ranks them; its first page only.</summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> SearchAsync(IntegrationConnectionSettings settings, MediaWorkKind kind, string title, int? year, CancellationToken cancellationToken = default);

    /// <summary>The films and shows the source files under an IMDb id; an episode's id finds none.</summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> FindByImdbIdAsync(IntegrationConnectionSettings settings, string imdbId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page (from 1) of the best-rated works of <paramref name="kind"/> with every genre of
    /// <paramref name="genreIds"/> and none of <paramref name="excludedGenreIds"/>, best first, among works
    /// enough people rated for the rating to mean something.
    /// </summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogWorkSummary>>> DiscoverAsync(IntegrationConnectionSettings settings, MediaWorkKind kind, IReadOnlyList<int> genreIds, IReadOnlyList<int> excludedGenreIds, int page, CancellationToken cancellationToken = default);

    /// <summary>Every genre the source files works of <paramref name="kind"/> under, with the role the core
    /// classifies works by.</summary>
    Task<IntegrationCallResult<IReadOnlyList<CatalogGenre>>> GetGenresAsync(IntegrationConnectionSettings settings, MediaWorkKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// The poster at <paramref name="posterPath"/>, a path a work of this source gave, in a size for a
    /// card. Fetched from the source's own image host without any credential (ADR 0007): the browser is
    /// never sent there.
    /// </summary>
    Task<IntegrationCallResult<CatalogImage>> GetPosterAsync(IntegrationConnectionSettings settings, string posterPath, CancellationToken cancellationToken = default);
}
