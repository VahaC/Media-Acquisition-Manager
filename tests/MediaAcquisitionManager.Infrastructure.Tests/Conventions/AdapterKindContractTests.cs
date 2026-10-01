using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Conventions;

// ADR 0005 — every adapter whose descriptor names a kind implements that kind's contract, so the
// registry never lists a type for a kind it can't serve (the type would pass its connection test and
// then fail at the first sync). Over the registry the app builds, so a new adapter is checked the
// moment it is registered. Kinds whose contracts are implemented so far: MediaLibrary (MAM-8);
// ReleaseSearch joins with MAM-10 and DownloadClient with MAM-11.
public sealed class AdapterKindContractTests
{
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
}
