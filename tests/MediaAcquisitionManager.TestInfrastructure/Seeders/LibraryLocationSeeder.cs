using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.TestInfrastructure.Data;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="ILibraryLocationSeeder"/>
public sealed class LibraryLocationSeeder(IServiceProvider services) : SeederBase(services), ILibraryLocationSeeder
{
    private ILibraryLocationDataFactory Locations => new LibraryLocationDataFactory(Db, Resolve<TimeProvider>());

    private ILibraryLocationFolderDataFactory Folders => new LibraryLocationFolderDataFactory(Db);

    public async Task<LibraryLocationEntity> Create(IntegrationConnectionEntity integration, string name, params string[] servicePaths)
    {
        ArgumentNullException.ThrowIfNull(integration);
        var location = await Locations.Create(integration.Id, integration.Name, Guid.NewGuid().ToString("N"), name);
        for (var position = 0; position < servicePaths.Length; position++)
            await Folders.Create(location.Id, servicePaths[position], position);
        return location;
    }
}
