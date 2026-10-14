using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A library work of a search that one of its releases may be (MAM-12).</summary>
/// <param name="WorkId">The work, one of those the search lists — those it lists by their words
/// included. MAM-32 — a series for a TV release, a movie for a movie release (ADR 0020).</param>
/// <param name="Match">Why the release matches it (<see cref="WorkMatching.Match"/>: never by words), and
/// which ids disagree — MAM-30, the strongest of what its title and its description claim.</param>
/// <param name="HeldEpisodeCount">MAM-32 — on a TV release's match, how many episodes of the series the
/// library holds among those <paramref name="Episodes"/> names; <c>null</c> on a movie's, and when it names
/// none.</param>
/// <param name="Episodes">MAM-30 — on a TV release's match, the seasons or episodes the claim that matched
/// names: its title's, or its description's when that matched the work more strongly (D15) — the other's when that
/// claim names none; <c>null</c> on a movie's, and when neither names any. What the replacement of old copies offers
/// (ADR 0032).</param>
public sealed record ReleaseLibraryMatch(Guid WorkId, WorkMatch Match, int? HeldEpisodeCount, ClaimedEpisodes? Episodes);
