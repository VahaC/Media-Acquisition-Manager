namespace MediaAcquisitionManager.Core.Search;

/// <summary>
/// The work a release's title claims to be (MAM-12): claims, like <see cref="ClaimedReleaseQuality"/> —
/// nothing here is verified, and nothing is assumed where the title doesn't say.
/// </summary>
/// <param name="Titles">Every title the release is listed under, in the title's order: one, or one per
/// side of a " / " ("Інтерстеллар / Interstellar"). Empty when the title names no year — MAM-32, and a TV
/// release's no season either — since the title can't then be told apart from its quality tail.</param>
/// <param name="Year">The release year it names; <c>null</c> when it names none. MAM-32 — the first year
/// of the span a TV release names, "(2018-2024)".</param>
/// <param name="LastYear">MAM-32 — the last year of that span; <c>null</c> for one year, and always for a
/// movie, whose range is a collection's span, not its year.</param>
/// <param name="Episodes">MAM-32 — the seasons, or the episodes of one season, a TV release's title
/// claims; <c>null</c> when it claims none, and always for a movie.</param>
public sealed record ReleaseWorkClaim(IReadOnlyList<string> Titles, int? Year, int? LastYear, ClaimedEpisodes? Episodes);
