using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — creates entries of a release source's feed of newest releases directly in the database.</summary>
public interface ISourceFeedEntryDataFactory
{
    /// <summary>
    /// A saved entry <paramref name="sourceItemId"/> of the release source <paramref name="sourceConnectionId"/> (named
    /// <paramref name="sourceName"/>), first seen at <paramref name="seenUtc"/> (the clock's now when <c>null</c>) and
    /// published at <paramref name="publishedUtc"/>; resolved at <paramref name="resolvedUtc"/> to
    /// <paramref name="catalogWorkId"/>, or not resolved yet when both are <c>null</c>.
    /// </summary>
    Task<SourceFeedEntryEntity> Create(
        Guid sourceConnectionId,
        string sourceName,
        string sourceItemId,
        string title,
        ReleaseKind kind = ReleaseKind.Movie,
        DateTime? seenUtc = null,
        DateTime? publishedUtc = null,
        DateTime? resolvedUtc = null,
        Guid? catalogWorkId = null);
}
