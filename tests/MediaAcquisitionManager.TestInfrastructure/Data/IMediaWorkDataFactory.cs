using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates media works, and the titles and identifiers sources assert for them, directly in
/// the database.</summary>
public interface IMediaWorkDataFactory
{
    /// <summary>A saved work with nothing else: every title and identifier is a row of its own.</summary>
    Task<MediaWorkEntity> Create(MediaWorkKind kind = MediaWorkKind.Movie);

    /// <summary>A saved title of <paramref name="workId"/> as the library integration
    /// <paramref name="sourceConnectionId"/> reported it in its item <paramref name="sourceItemId"/>.</summary>
    Task<MediaWorkTitleEntity> AddTitle(
        Guid workId,
        string text,
        MediaTitleKind kind,
        int? year,
        Guid? sourceConnectionId,
        string? sourceName,
        string? sourceItemId);

    /// <summary>A saved registry id of <paramref name="workId"/> as the library integration
    /// <paramref name="sourceConnectionId"/> reported it in its item <paramref name="sourceItemId"/>.</summary>
    Task<MediaWorkIdentifierEntity> AddIdentifier(
        Guid workId,
        MediaIdentifierScheme scheme,
        string value,
        Guid? sourceConnectionId,
        string? sourceName,
        string? sourceItemId);
}
