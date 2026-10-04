using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates release snapshots directly in the database.</summary>
public interface IReleaseDataFactory
{
    /// <summary>
    /// A saved snapshot of a release listed by the search integration
    /// <paramref name="sourceConnectionId"/>, its download link sealed like a credential with the
    /// purpose <c>ReleasePayloadLocatorPurpose.For</c>. The default link carries a planted API key, the
    /// way a gateway's link does (ADR 0004). Given <paramref name="searchSourceId"/>, it is a release that
    /// stored search's source found (MAM-12). MAM-13 — the source's id for the listing, the info hash it
    /// states and the payload kind it names, when a test needs a listing that says them; MAM-32 — the
    /// categories it is listed in, comma-separated as the release store keeps them.
    /// </summary>
    Task<ReleaseEntity> Create(
        Guid? sourceConnectionId,
        string sourceName = "Torznab",
        string title = "Interstellar (2014)",
        string locator = "http://127.0.0.1:9/1/download?apikey=planted-release-key",
        Guid? searchSourceId = null,
        string? sourceItemId = null,
        string? infoHash = null,
        ReleasePayloadKind? payloadKind = null,
        string? categoryIds = null);
}
