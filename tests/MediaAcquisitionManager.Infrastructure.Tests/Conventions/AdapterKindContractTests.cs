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
// MAM-34 — a library's identifier writer is optional: a library type without it keeps the owner's links in the
// app, so no library is required to have one; but one is only ever a further contract of a media library, and it names
// the kinds of item it writes — films and shows at most, Jellyfin's films only — with a sentence for the rest.
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

    [Fact]
    public void Jellyfin_writes_identifiers_and_every_identifier_writer_is_a_media_library()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var registry = harness.Registry;

        //Act
        var writers = registry.Descriptors
            .Where(descriptor => registry.Find<IMediaLibraryIdentifierWriter>(descriptor.TypeId) is not null)
            .ToList();

        //Assert
        // Guard the guard: Jellyfin's writer is the one a link is written through (MAM-34).
        Assert.Contains(writers, descriptor => descriptor.TypeId == "jellyfin");
        // The writer sets a library item's ids: a type of another kind, or one that can't list the items, has none
        // to set.
        Assert.All(writers, descriptor =>
        {
            Assert.Equal(IntegrationKind.MediaLibrary, descriptor.Kind);
            Assert.NotNull(registry.Find<IMediaLibraryProvider>(descriptor.TypeId));
        });
    }

    [Fact]
    public void Every_identifier_writer_writes_only_films_or_shows_and_says_why_it_keeps_the_rest_in_the_app()
    {
        //Arrange
        using var harness = new IntegrationAdapterHarness();
        var registry = harness.Registry;

        //Act
        var writers = registry.Descriptors
            .Select(descriptor => (descriptor.TypeId, Writer: registry.Find<IMediaLibraryIdentifierWriter>(descriptor.TypeId)))
            .Where(found => found.Writer is not null)
            .ToList();

        //Assert
        // A link to the work itself exists only for a film or a show; an episode's link is to its show, never written.
        Assert.All(writers, found =>
        {
            Assert.NotEmpty(found.Writer!.WritableKinds);
            Assert.All(found.Writer.WritableKinds, kind => Assert.Contains(kind, (MediaWorkKind[])[MediaWorkKind.Movie, MediaWorkKind.Series]));
            Assert.All(
                Enum.GetValues<MediaWorkKind>().Except(found.Writer.WritableKinds),
                kind => Assert.False(string.IsNullOrWhiteSpace(found.Writer.UnwritableKindReason(kind))));
        });
        // Jellyfin's save of a series rewrites all its seasons and episodes (ADR 0026): films only.
        Assert.Equal([MediaWorkKind.Movie], writers.Single(found => found.TypeId == "jellyfin").Writer!.WritableKinds);
    }
}
