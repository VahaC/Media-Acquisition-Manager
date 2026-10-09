namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>What a stored search's releases are matched against (MAM-12): the library works the search lists, and where
/// each series' episodes are placed (MAM-32).</summary>
/// <param name="Works">The works the search lists — those it lists by their words included —, in its order.</param>
/// <param name="Episodes">Each listed series' episodes, by series: the episode work and its place, one per library
/// item that places it.</param>
public sealed record ReleaseMatchWorks(
    IReadOnlyList<LibraryWorkMatch> Works,
    ILookup<Guid, (Guid EpisodeId, int? Season, int? Episode)> Episodes);
