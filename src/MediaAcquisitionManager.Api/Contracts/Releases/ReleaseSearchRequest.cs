namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A release search: what to look for in every enabled release source.</summary>
/// <remarks>
/// MAM-10 — every field a nullable string, the year included: a missing title or a year that isn't one
/// must reach the search rules, whose sentence the page shows, not stop at a validation body with no
/// <c>error</c> in it (the <see cref="CreateIntegrationRequest"/> and <see cref="LibraryWorkListRequest"/>
/// reasons).
/// </remarks>
/// <param name="Text">The title to search for; required, at most 200 characters.</param>
/// <param name="Year">The year, such as <c>2014</c>; blank for none.</param>
/// <param name="ImdbId">The IMDb id, such as <c>tt0816692</c>, for the sources that can search by it;
/// blank for none.</param>
/// <param name="TmdbId">The TMDb id, such as <c>157336</c>, for the sources that can search by it; blank
/// for none.</param>
public sealed record ReleaseSearchRequest(
    string? Text = null,
    string? Year = null,
    string? ImdbId = null,
    string? TmdbId = null);
