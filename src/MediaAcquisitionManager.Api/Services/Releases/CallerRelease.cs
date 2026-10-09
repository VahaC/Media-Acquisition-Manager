using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>A release of one of the caller's own stored searches (<see cref="CallerSearches"/>), with that search and
/// the place its source has in it.</summary>
/// <param name="Release">The stored release.</param>
/// <param name="Search">The caller's search that found it.</param>
/// <param name="SourcePosition">Its source's place in the search.</param>
/// <param name="SourceName">Its source's name when it was searched, as the search's source row keeps it.</param>
public sealed record CallerRelease(ReleaseEntity Release, SearchEntity Search, int SourcePosition, string SourceName);
