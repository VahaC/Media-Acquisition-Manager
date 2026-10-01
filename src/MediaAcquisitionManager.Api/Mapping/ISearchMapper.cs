using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Search;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>Turns a stored search, its sources and its releases into the search page's contracts
/// (MAM-12).</summary>
public interface ISearchMapper
{
    /// <summary>
    /// A stored search: what was asked; the library works that match, as <paramref name="library"/> orders
    /// them, each with the ids and the title that matched and who claimed them; the sources in the order
    /// given, each with how many releases it listed (<paramref name="releaseCounts"/>, by source row id) and
    /// its sentence only when <paramref name="forOwner"/> — it can name the service's address; and what the
    /// titles of all its releases (<paramref name="releaseTitles"/>) claim, for the list's filters.
    /// </summary>
    SearchResponse ToResponse(
        SearchEntity search,
        IReadOnlyList<LibraryWorkMatch> library,
        IReadOnlyList<SearchSourceEntity> sources,
        IReadOnlyDictionary<Guid, int> releaseCounts,
        IReadOnlyList<string> releaseTitles,
        bool forOwner);

    /// <summary>A release of the search with what its title and listing claim, and the search's works it
    /// matches.</summary>
    SearchReleaseDto ToDto(SearchRelease release);
}
