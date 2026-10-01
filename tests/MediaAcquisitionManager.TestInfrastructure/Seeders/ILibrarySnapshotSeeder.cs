using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <summary>
/// A library snapshot for a test, stored the way the app stores one: by a whole sync through the app's
/// own runner and snapshot store (MAM-8), so the works, versions, files and libraries are exactly what a
/// sync makes of what the fake service answers.
/// </summary>
public interface ILibrarySnapshotSeeder
{
    /// <summary>
    /// Plants a queued run of <paramref name="integration"/> requested by <paramref name="requestedBy"/>,
    /// runs it to its end against whatever the adapter's network answers — arm the factory's handler
    /// first, e.g. with <c>FakeJellyfinServer</c> — and returns the run as stored: succeeded with its
    /// counts, or failed with its error.
    /// </summary>
    Task<LibrarySyncRunEntity> Sync(IntegrationConnectionEntity integration, UserEntity? requestedBy = null);
}
