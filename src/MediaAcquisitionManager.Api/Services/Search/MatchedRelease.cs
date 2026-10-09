using MediaAcquisitionManager.Api.Services.Releases;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>MAM-24 — one of the caller's releases with the library works it matches, read as the search page reads it
/// (<see cref="IReleaseMatchReader.MatchAsync"/>).</summary>
/// <param name="Found">The release with the caller's search that found it.</param>
/// <param name="Release">The release with what its title and listing claim and the search's works it matches.</param>
/// <param name="Works">The works it was matched against, the search's own.</param>
public sealed record MatchedRelease(CallerRelease Found, SearchRelease Release, ReleaseMatchWorks Works);
