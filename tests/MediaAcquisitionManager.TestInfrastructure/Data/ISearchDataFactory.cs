using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates stored searches, and how each release source answered them, directly in the database
/// (MAM-12).</summary>
public interface ISearchDataFactory
{
    /// <summary>A saved search by <paramref name="userId"/> with no sources: each is a row of its own.</summary>
    Task<SearchEntity> Create(Guid userId, string text = "Interstellar", int? year = null, string? imdbId = null, string? tmdbId = null);

    /// <summary>
    /// A saved answer of the release source <paramref name="integrationConnectionId"/> to
    /// <paramref name="searchId"/>, under the name <paramref name="name"/> copied the way the search keeps
    /// it, at <paramref name="position"/> among the search's sources.
    /// </summary>
    Task<SearchSourceEntity> AddSource(
        Guid searchId,
        Guid? integrationConnectionId,
        string name,
        int position = 0,
        IntegrationCallStatus? status = IntegrationCallStatus.Succeeded,
        ReleaseSourceNotSearchedReason? notSearchedReason = null,
        string? error = null);
}
