using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-34 — library works' cached TMDb searches for a test: what an earlier preparation found.</summary>
public interface ILibraryLinkSearchSeeder
{
    /// <summary>
    /// A search of <paramref name="work"/> under <paramref name="queryKey"/> that found <paramref name="candidates"/>, in
    /// that order, marked "none fits" by <paramref name="noneFitsBy"/> at <paramref name="noneFitsUtc"/> when given.
    /// </summary>
    Task<LibraryLinkSearchEntity> Create(
        MediaWorkEntity work,
        string queryKey,
        IReadOnlyList<(MediaWorkKind Kind, string CatalogId, LibraryLinkCandidateSource Source)> candidates,
        DateTime? searchedUtc = null,
        DateTime? noneFitsUtc = null,
        UserEntity? noneFitsBy = null);
}
