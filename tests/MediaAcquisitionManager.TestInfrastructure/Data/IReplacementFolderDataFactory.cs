using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates library folders ticked for replacement directly in the database (MAM-24).</summary>
public interface IReplacementFolderDataFactory
{
    /// <summary>A saved tick on <paramref name="path"/>, stored as given (canonical or not, a library folder or not), at
    /// <paramref name="position"/>.</summary>
    Task<ReplacementFolderEntity> Create(string path, int position = 0);
}
