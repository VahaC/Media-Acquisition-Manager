using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>Creates the replacement switch's one row directly in the database (MAM-24).</summary>
public interface IReplacementSettingsDataFactory
{
    /// <summary>The saved switch row, on unless <paramref name="isEnabled"/> says otherwise. There is one at most: a
    /// second call fails on its key.</summary>
    Task<ReplacementSettingsEntity> Create(bool isEnabled = true);
}
