using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Release snapshots for a test, as a release search integration listed them.</summary>
public interface IReleaseSeeder
{
    /// <summary>A release snapshot listed by <paramref name="source"/>, with its id and name as the
    /// provenance of the row. MAM-33 — titled <paramref name="title"/> (the factory's default when <c>null</c>), listed as
    /// <paramref name="kind"/> in <paramref name="categoryIds"/>, with the listing's <paramref name="description"/> and
    /// TMDb id, when a test matches it to a recommended work.</summary>
    Task<ReleaseEntity> Create(
        IntegrationConnectionEntity source,
        string? title = null,
        ReleaseKind kind = ReleaseKind.Movie,
        string? description = null,
        string? tmdbId = null,
        string? categoryIds = null);

    /// <summary>
    /// MAM-13 — a release one of <paramref name="user"/>'s searches found a moment ago through
    /// <paramref name="source"/>: a stored search of theirs, the source's answer to it, and the release, the
    /// one a Download dialog sends. Its download link is <paramref name="locator"/>, or by default one on the
    /// source's own feed carrying the API key placeholder and a planted <c>link=</c> token — the shape the
    /// Torznab adapter stores, which a <c>FakeTorznabServer</c> at the source's address answers with its
    /// .torrent. MAM-32 — listed in <paramref name="categoryIds"/>, comma-separated as the release store
    /// keeps them, or in none. With the listing's <paramref name="description"/>, which names its job when given.
    /// </summary>
    Task<ReleaseEntity> CreateFound(
        UserEntity user,
        IntegrationConnectionEntity source,
        string title = "Interstellar (2014) BDRip 1080p",
        string? locator = null,
        string? sourceItemId = null,
        string? infoHash = null,
        ReleasePayloadKind? payloadKind = null,
        string? categoryIds = null,
        string? description = null);

    /// <summary>
    /// MAM-24 — a release titled <paramref name="title"/>, listed as <paramref name="kind"/> with the IMDb and TMDb ids
    /// given, that <paramref name="user"/>'s search for <paramref name="searchText"/> of <paramref name="searchYear"/> (by
    /// those ids too) found a moment ago through <paramref name="source"/>: the search the release is matched to the
    /// library through, as the search page and the Download dialog match it. A magnet when <paramref name="payloadKind"/>
    /// says so. MAM-30 — with the listing's <paramref name="description"/>, which the matching reads too.
    /// </summary>
    Task<ReleaseEntity> CreateFoundBySearch(
        UserEntity user,
        IntegrationConnectionEntity source,
        string searchText,
        int? searchYear,
        string title,
        ReleaseKind kind = ReleaseKind.Movie,
        string? imdbId = null,
        string? tmdbId = null,
        ReleasePayloadKind? payloadKind = null,
        string? description = null);
}
