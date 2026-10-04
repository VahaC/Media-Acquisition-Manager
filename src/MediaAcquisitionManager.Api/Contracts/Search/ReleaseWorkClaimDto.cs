namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Which work a release's title claims to be (MAM-12): claims, never verified.</summary>
/// <param name="Titles">Every title it is listed under, in the title's order; empty when the title names no
/// year — MAM-32, and a TV release's no season either — as the title can't then be told apart from its
/// quality.</param>
/// <param name="Year">The year it names; <c>null</c> when it names none. MAM-32 — the first year of a TV
/// release's span.</param>
/// <param name="LastYear">MAM-32 — the last year of the span a TV release names, "(2018-2024)"; <c>null</c>
/// for one year, and always for a movie.</param>
/// <param name="Episodes">MAM-32 — the seasons, or the episodes of one season, a TV release's title claims;
/// <c>null</c> when it claims none, and always for a movie.</param>
public sealed record ReleaseWorkClaimDto(IReadOnlyList<string> Titles, int? Year, int? LastYear, ClaimedEpisodesDto? Episodes);
