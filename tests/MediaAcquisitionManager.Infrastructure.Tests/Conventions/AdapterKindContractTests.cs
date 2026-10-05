using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Conventions;

// ADR 0005 — every adapter whose descriptor names a kind implements that kind's contract, so the
// registry never lists a type for a kind it can't serve (the type would pass its connection test and
// then fail at the first sync). Over the registry the app builds, so a new adapter is checked the
// moment it is registered. Every kind's contract is implemented now: MediaLibrary (MAM-8),
// ReleaseSearch (MAM-10), DownloadClient (MAM-11) and Metadata (MAM-33), whose kind takes two contracts.
public sealed class AdapterKindContractTests
{
    [Fact]
    public void Every_release_search_adapter_implements_the_release_search_contract()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var registry = harness.Registry;

        //Act
        var sources = registry.Descriptors
            .Where(descriptor => descriptor.Kind == IntegrationKind.ReleaseSearch)
            .Select(descriptor => descriptor.TypeId)
            .ToList();
        var withoutContract = sources.Where(typeId => registry.Find<IReleaseSearchProvider>(typeId) is null).ToList();

        //Assert
        // Guard the guard: a scan that found no release search type would pass whatever the adapters do.
        Assert.NotEmpty(sources);
        Assert.Empty(withoutContract);
    }

    [Fact]
    public void Every_media_library_adapter_implements_the_media_library_contract()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var registry = harness.Registry;

        //Act
        var libraries = registry.Descriptors
            .Where(descriptor => descriptor.Kind == IntegrationKind.MediaLibrary)
            .Select(descriptor => descriptor.TypeId)
            .ToList();
        var withoutContract = libraries.Where(typeId => registry.Find<IMediaLibraryProvider>(typeId) is null).ToList();

        //Assert
        // Guard the guard: a scan that found no media library type would pass whatever the adapters do.
        Assert.NotEmpty(libraries);
        Assert.Empty(withoutContract);
    }

    [Fact]
    public void Every_download_client_adapter_implements_the_download_client_contract()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var registry = harness.Registry;

        //Act
        var clients = registry.Descriptors
            .Where(descriptor => descriptor.Kind == IntegrationKind.DownloadClient)
            .Select(descriptor => descriptor.TypeId)
            .ToList();
        var withoutContract = clients.Where(typeId => registry.Find<IDownloadClient>(typeId) is null).ToList();

        //Assert
        // Guard the guard: a scan that found no download client type would pass whatever the adapters do.
        Assert.NotEmpty(clients);
        Assert.Empty(withoutContract);
    }

    [Fact]
    public void Every_metadata_adapter_implements_the_resolver_and_the_catalog_contracts()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var registry = harness.Registry;

        //Act
        var catalogs = registry.Descriptors
            .Where(descriptor => descriptor.Kind == IntegrationKind.Metadata)
            .Select(descriptor => descriptor.TypeId)
            .ToList();
        var withoutContract = catalogs
            .Where(typeId => registry.Find<IMetadataResolver>(typeId) is null || registry.Find<IMetadataCatalog>(typeId) is null)
            .ToList();

        //Assert
        // Guard the guard: a scan that found no metadata type would pass whatever the adapters do.
        Assert.NotEmpty(catalogs);
        Assert.Empty(withoutContract);
    }
}
