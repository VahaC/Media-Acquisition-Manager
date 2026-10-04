namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A search of the search page: what to look for in the library and in every enabled release
/// source (MAM-12).</summary>
/// <remarks>
/// Separate fields, not one line to parse (Vasyl's decision): the title, then the year and the ids when
/// they are known. Every field a nullable string for the <see cref="ReleaseSearchRequest"/> reason — a
/// missing title or a year that isn't one reaches the rules' sentence, not a validation body — and the
/// same rules and sentences as that search.
/// </remarks>
/// <param name="Text">The title to search for; required, at most 200 characters.</param>
/// <param name="Year">The year, such as <c>2014</c>; blank for none.</param>
/// <param name="ImdbId">The IMDb id, such as <c>tt0816692</c>; blank for none.</param>
/// <param name="TmdbId">The TMDb id, such as <c>157336</c>; blank for none.</param>
/// <param name="Season">The season, such as <c>1</c> (<c>0</c> for the specials), searched for in TV alone
/// (MAM-32); blank for none.</param>
public sealed record SearchRequest(
    string? Text = null,
    string? Year = null,
    string? ImdbId = null,
    string? TmdbId = null,
    string? Season = null);
