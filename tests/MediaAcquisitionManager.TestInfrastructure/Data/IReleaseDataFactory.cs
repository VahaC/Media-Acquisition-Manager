using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates release snapshots directly in the database.</summary>
public interface IReleaseDataFactory
{
    /// <summary>
    /// A saved snapshot of a release listed by the search integration
    /// <paramref name="sourceConnectionId"/>, its download link sealed like a credential with the
    /// purpose <see cref="ReleaseDataFactory.PayloadLocatorPurpose"/>. The default link carries a
    /// planted API key, the way a gateway's link does (ADR 0004). Given
    /// <paramref name="searchSourceId"/>, it is a release that stored search's source found (MAM-12).
    /// </summary>
    Task<ReleaseEntity> Create(
        Guid? sourceConnectionId,
        string sourceName = "Torznab",
        string title = "Interstellar (2014)",
        string locator = "http://127.0.0.1:9/1/download?apikey=planted-release-key",
        Guid? searchSourceId = null);
}
