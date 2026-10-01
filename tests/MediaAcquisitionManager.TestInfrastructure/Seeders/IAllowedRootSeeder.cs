using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Allowed roots for a test (MAM-9), planted without the settings page.</summary>
public interface IAllowedRootSeeder
{
    /// <summary>Allows <paramref name="paths"/>, in the order given.</summary>
    Task<IReadOnlyList<AllowedRootEntity>> Allow(params string[] paths);
}
