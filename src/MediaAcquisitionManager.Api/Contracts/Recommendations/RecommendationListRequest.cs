namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — the query string of a page of one recommendation tab: its filters and the page, all applied by the
/// server.</summary>
/// <remarks>
/// Every value a string, for the <see cref="SearchReleaseListRequest"/> reason: a wrong one — a page that isn't a number
/// included — answers the rules' <c>{ error }</c> sentence, not a validation body. A blank filter filters nothing.
/// </remarks>
/// <param name="Tab"><c>Movies</c>, <c>Series</c>, <c>Cartoons</c> or <c>Documentaries</c>, in any letter case;
/// required.</param>
/// <param name="Reason">A reason kind by name (<c>Collection</c>): only the works recommended for it.</param>
/// <param name="Available"><c>true</c>: only the works the last check found at a release source.</param>
/// <param name="GenreId">A catalog genre id (<c>18</c>): only the works the catalog files under it.</param>
/// <param name="Decade">A decade's first year (<c>1990</c>): only the works released in it.</param>
/// <param name="Kind"><c>Movie</c> or <c>Series</c>: only films or only shows.</param>
/// <param name="Sort">The order, by name: <c>Score</c> (the best recommended first; when blank), <c>Rating</c>,
/// <c>Votes</c>, <c>Newest</c>, <c>Oldest</c>, <c>Title</c> or <c>Releases</c>. A tie falls back to the score's
/// order.</param>
/// <param name="Page">Which page, counted from 1; 1 when blank.</param>
/// <param name="PageSize">Works per page, 1 to 100; 25 when blank.</param>
public sealed record RecommendationListRequest(
    string? Tab = null,
    string? Reason = null,
    string? Available = null,
    string? GenreId = null,
    string? Decade = null,
    string? Kind = null,
    string? Sort = null,
    string? Page = null,
    string? PageSize = null);
