using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A library work of a search that one of its releases may be (MAM-12).</summary>
/// <param name="WorkId">The work, one of those the search lists — those it lists by their words
/// included. MAM-32 — a series for a TV release, a movie for a movie release (ADR 0020).</param>
/// <param name="Match">Why the release matches it (<see cref="WorkMatching.Match"/>: never by words), and
/// which ids disagree.</param>
/// <param name="HeldEpisodeCount">MAM-32 — on a TV release's match, how many episodes of the series the
/// library holds among those the release's title claims; <c>null</c> on a movie's, and when the title
/// claims none.</param>
public sealed record ReleaseLibraryMatch(Guid WorkId, WorkMatch Match, int? HeldEpisodeCount);
