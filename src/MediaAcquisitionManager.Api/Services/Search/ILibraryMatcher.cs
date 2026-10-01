using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>
/// Finds the library works a search is for (MAM-12), in the snapshot the library syncs imported (MAM-8):
/// no media server is asked, so the library's side answers as fast as the database and as it is shown on
/// the Library page.
/// </summary>
public interface ILibraryMatcher
{
    /// <summary>
    /// Every work that matches <paramref name="query"/> (<see cref="WorkMatching"/>: an id, then a title
    /// and the year, then a title alone), strongest evidence first, then by the title the Library page
    /// shows in its order, then by id. A file's path problem and why it couldn't be inspected only
    /// <paramref name="forOwner"/>, as on the Library page.
    /// </summary>
    Task<IReadOnlyList<LibraryWorkMatch>> MatchAsync(WorkClaim query, bool forOwner, CancellationToken cancellationToken = default);
}
