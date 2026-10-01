namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>
/// The search page (MAM-12): one search asks the library and every enabled release source at once and is
/// stored, so every filter, sort and page reads what it stored instead of asking the sources again
/// (ADR 0011). A search is its creator's, and lives a day, as long as its releases. Every signed-in user
/// searches (MAM-10's choice); only the owner reads why a source failed or a file has no app path.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Validates the search by <c>ReleaseSearchRules</c> (MAM-10's sentences); then reads the library's
    /// matching works and asks every enabled release source, the two at once; stores the search, how each
    /// source answered and every release found, linked to its source; and removes what is more than a day
    /// old on the way. <see cref="SearchCreateStatus.Invalid"/>, with nothing stored and no source asked,
    /// for a missing or too long title, a year that isn't one, or an id that isn't an IMDb or a TMDb id.
    /// The sentence of a failed source, and a file's path problem, only when <paramref name="callerUserId"/>
    /// is the installation owner.
    /// </summary>
    Task<SearchCreateResult> CreateAsync(
        string? text,
        string? year,
        string? imdbId,
        string? tmdbId,
        Guid callerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's search: the library's side matched again now, the sources as they answered.
    /// <see cref="SearchReadStatus.NotFound"/> when no search has <paramref name="id"/>, it is another
    /// user's, or it is more than a day old.
    /// </summary>
    Task<SearchReadResult> GetAsync(Guid id, Guid callerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the caller's search's releases, filtered by <paramref name="source"/> (a search source
    /// id), <paramref name="language"/> (a claimed audio language, or <c>unknown</c>),
    /// <paramref name="quality"/> (a claimed resolution, or <c>unknown</c>) and the size bounds, ordered by
    /// <paramref name="sort"/> (<c>SearchReleaseListRules</c>); each with the search's library works it
    /// matches. <see cref="SearchReleaseListStatus.NotFound"/> as <see cref="GetAsync"/>, checked first;
    /// then <see cref="SearchReleaseListStatus.Invalid"/> for a page below 1, a page size outside 1 to 100,
    /// or a filter or an order the list doesn't take.
    /// </summary>
    Task<SearchReleaseListResult> ListReleasesAsync(
        Guid id,
        string? source,
        string? language,
        string? quality,
        string? minSizeBytes,
        string? maxSizeBytes,
        string? sort,
        int page,
        int pageSize,
        Guid callerUserId,
        CancellationToken cancellationToken = default);
}
