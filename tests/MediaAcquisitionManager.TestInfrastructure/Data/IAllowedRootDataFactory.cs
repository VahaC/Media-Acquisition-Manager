using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates allowed roots directly in the database (MAM-9).</summary>
public interface IAllowedRootDataFactory
{
    /// <summary>A saved allowed root at <paramref name="path"/>, stored as given (canonical or not), at
    /// <paramref name="position"/>.</summary>
    Task<AllowedRootEntity> Create(string path, int position = 0);
}
