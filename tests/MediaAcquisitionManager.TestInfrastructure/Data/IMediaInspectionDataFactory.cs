using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates what the media inspector read, kept by the file's identity, directly in the database (MAM-9).</summary>
public interface IMediaInspectionDataFactory
{
    /// <summary>
    /// A saved reading of the bytes the key <paramref name="deviceId"/>, <paramref name="inode"/>,
    /// <paramref name="sizeBytes"/> and <paramref name="modifiedUtc"/> names, with <paramref name="tracks"/> in the
    /// order given, the way an inspection run stores one; a cached failure when <paramref name="error"/> is
    /// given. No file name points at it until one is linked (<see cref="IPhysicalFileDataFactory.Link"/>).
    /// </summary>
    Task<MediaInspectionEntity> Create(
        long deviceId,
        long inode,
        long sizeBytes,
        DateTime modifiedUtc,
        IReadOnlyList<MediaInspectionTrackEntity>? tracks = null,
        Message? error = null,
        long? durationMilliseconds = null,
        long? bitRate = null);
}
