namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A stored search (MAM-12): what was asked, what the library holds of it, and how every release
/// source answered.</summary>
/// <remarks>
/// The releases themselves are read a page at a time, filtered and sorted by the server
/// (<see cref="SearchReleaseListResponse"/>): this answer says what there is to filter by. The library's
/// side is matched again on every read, so a sync since the search shows; the sources are as they
/// answered when the search was made.
/// </remarks>
/// <param name="Id">The search, for reading it and its releases again.</param>
/// <param name="Text">The title searched for, trimmed.</param>
/// <param name="Year">The year asked for; <c>null</c> when none was.</param>
/// <param name="ImdbId">The IMDb id asked for, trimmed; <c>null</c> when none was.</param>
/// <param name="TmdbId">The TMDb id asked for, trimmed; <c>null</c> when none was.</param>
/// <param name="Season">The season asked for (MAM-32); <c>null</c> when none was.</param>
/// <param name="CreatedUtc">When the search was made; it is removed a day later.</param>
/// <param name="Library">Every library work that matches, strongest evidence first; then, MAM-12, at most
/// 20 works that don't but whose one title or one file path holds every word of the title, evidence
/// <c>Words</c> — not a match, there for the person to look at (ADR 0012). Empty when none of either.</param>
/// <param name="LibraryUnlistedCount">How many more works hold every word of the title than
/// <paramref name="Library"/> lists at the end; 0 when none were left out.</param>
/// <param name="Sources">Every release source the search asked or meant to, by its place in the search:
/// name order when it was made.</param>
/// <param name="Facets">What the search's releases claim, for the list's filters.</param>
public sealed record SearchResponse(
    Guid Id,
    string Text,
    int? Year,
    string? ImdbId,
    string? TmdbId,
    int? Season,
    DateTime CreatedUtc,
    IReadOnlyList<LibraryMatchDto> Library,
    int LibraryUnlistedCount,
    IReadOnlyList<SearchSourceDto> Sources,
    SearchFacetsDto Facets);
