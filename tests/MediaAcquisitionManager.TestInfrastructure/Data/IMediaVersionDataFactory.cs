using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates versions of a media work directly in the database.</summary>
public interface IMediaVersionDataFactory
{
    /// <summary>
    /// A saved version of <paramref name="workId"/> as a library reported it: its item
    /// <paramref name="sourceItemId"/> and, when the item groups several versions, the version's own
    /// id inside it (<paramref name="sourceVersionId"/>).
    /// </summary>
    Task<MediaVersionEntity> Create(
        Guid workId,
        string? sourceItemId,
        string? sourceVersionId = null,
        string? editionLabel = null,
        Guid? sourceConnectionId = null,
        string? sourceName = null);
}
