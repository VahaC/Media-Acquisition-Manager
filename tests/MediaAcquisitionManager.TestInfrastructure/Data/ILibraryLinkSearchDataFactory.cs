using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-34 — creates library works' cached TMDb searches, with their candidates, directly in the
/// database.</summary>
public interface ILibraryLinkSearchDataFactory
{
    /// <summary>
    /// A saved search of the work <paramref name="mediaWorkId"/> under <paramref name="queryKey"/>, run at
    /// <paramref name="searchedUtc"/> (the clock's now when <c>null</c>), with <paramref name="candidates"/> in their
    /// order (their positions from 0) and the owner's "none fits" when <paramref name="noneFitsUtc"/> is given. Stored as
    /// given: the candidates' catalog works need no row of their own.
    /// </summary>
    Task<LibraryLinkSearchEntity> Create(
        Guid mediaWorkId,
        string queryKey,
        IReadOnlyList<(MediaWorkKind Kind, string CatalogId, LibraryLinkCandidateSource Source)> candidates,
        DateTime? searchedUtc = null,
        DateTime? noneFitsUtc = null,
        Guid? noneFitsByUserId = null);
}
