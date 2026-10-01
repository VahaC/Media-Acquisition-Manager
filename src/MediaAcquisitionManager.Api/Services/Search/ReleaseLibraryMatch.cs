using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A library work of a search that one of its releases may be (MAM-12).</summary>
/// <param name="WorkId">The work, one of those the search lists — those it lists by their words
/// included.</param>
/// <param name="Match">Why the release matches it (<see cref="WorkMatching.Match"/>: never by words), and
/// which ids disagree.</param>
public sealed record ReleaseLibraryMatch(Guid WorkId, WorkMatch Match);
