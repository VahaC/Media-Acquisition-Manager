using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A library work of a search that one of its releases may be (MAM-12).</summary>
/// <param name="WorkId">The work, one of the search's library matches.</param>
/// <param name="Match">Why the release matches it, and which ids disagree.</param>
public sealed record ReleaseLibraryMatch(Guid WorkId, WorkMatch Match);
