using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Release snapshots for a test, as a release search integration listed them.</summary>
public interface IReleaseSeeder
{
    /// <summary>A release snapshot listed by <paramref name="source"/>, with its id and name as the
    /// provenance of the row.</summary>
    Task<ReleaseEntity> Create(IntegrationConnectionEntity source);

    /// <summary>
    /// MAM-13 — a release one of <paramref name="user"/>'s searches found a moment ago through
    /// <paramref name="source"/>: a stored search of theirs, the source's answer to it, and the release, the
    /// one a Download dialog sends. Its download link is <paramref name="locator"/>, or by default one on the
    /// source's own feed carrying the API key placeholder and a planted <c>link=</c> token — the shape the
    /// Torznab adapter stores, which a <c>FakeTorznabServer</c> at the source's address answers with its
    /// .torrent. MAM-32 — listed in <paramref name="categoryIds"/>, comma-separated as the release store
    /// keeps them, or in none.
    /// </summary>
    Task<ReleaseEntity> CreateFound(
        UserEntity user,
        IntegrationConnectionEntity source,
        string title = "Interstellar (2014) BDRip 1080p",
        string? locator = null,
        string? sourceItemId = null,
        string? infoHash = null,
        ReleasePayloadKind? payloadKind = null,
        string? categoryIds = null);
}
