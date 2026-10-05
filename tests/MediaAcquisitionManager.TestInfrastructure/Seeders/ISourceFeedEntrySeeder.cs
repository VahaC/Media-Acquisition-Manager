using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>MAM-33 — entries of a release source's feed of newest releases for a test, planted as the feed reader
/// stores them.</summary>
public interface ISourceFeedEntrySeeder
{
    /// <summary>
    /// An entry of <paramref name="source"/> titled <paramref name="title"/>, first seen at <paramref name="seenUtc"/>
    /// (now when <c>null</c>) and published at <paramref name="publishedUtc"/>, resolved — at the time it was seen — to
    /// <paramref name="resolvedTo"/>, or left unresolved when that is <c>null</c>. <paramref name="sourceItemId"/> is the
    /// source's id of the release — a fresh one of a Torznab guid's shape when <c>null</c>.
    /// </summary>
    Task<SourceFeedEntryEntity> Create(
        IntegrationConnectionEntity source,
        string title,
        CatalogWorkEntity? resolvedTo,
        DateTime? seenUtc = null,
        DateTime? publishedUtc = null,
        ReleaseKind kind = ReleaseKind.Movie,
        string? sourceItemId = null);
}
