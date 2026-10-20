using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>Library sync runs for a test, planted in any status without running anything (MAM-8).</summary>
public interface ILibrarySyncRunSeeder
{
    /// <summary>
    /// A run of <paramref name="integration"/> in <paramref name="status"/>, with the integration's name
    /// copied the way the app copies it, requested by <paramref name="requestedBy"/> (id and user name)
    /// at <paramref name="createdUtc"/> — the clock's now when <c>null</c>; give runs explicit times when
    /// a test compares them, so "latest" never depends on two rows sharing an instant.
    /// </summary>
    Task<LibrarySyncRunEntity> Create(
        IntegrationConnectionEntity integration,
        LibrarySyncStatus status,
        UserEntity? requestedBy = null,
        DateTime? createdUtc = null,
        Message? error = null);
}
