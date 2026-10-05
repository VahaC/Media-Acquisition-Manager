using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates the hashes of file contents, kept by the file's identity, directly in the database (MAM-19).</summary>
public interface IFileHashDataFactory
{
    /// <summary>
    /// A saved hash <paramref name="sha256"/> of the bytes the key <paramref name="deviceId"/>,
    /// <paramref name="inode"/>, <paramref name="sizeBytes"/> and <paramref name="modifiedUtc"/> names, the way an
    /// inspection run stores one, hashed at <paramref name="createdUtc"/> (the clock's now when <c>null</c>).
    /// Values are stored as given, so a test can plant a row the app would never write.
    /// </summary>
    Task<FileHashEntity> Create(long deviceId, long inode, long sizeBytes, DateTime modifiedUtc, string sha256, DateTime? createdUtc = null);
}
