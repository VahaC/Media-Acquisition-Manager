using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Integrations;

public sealed class IntegrationAdapterRegistryTests
{
    [Fact]
    public void Descriptors_are_ordered_by_kind_then_name()
    {
        //Arrange
        IIntegrationAdapter[] adapters =
        [
            Adapter("zeta-client", "zeta client", IntegrationKind.DownloadClient),
            Adapter("search", "Search", IntegrationKind.ReleaseSearch),
            Adapter("beta-library", "beta library", IntegrationKind.MediaLibrary),
            Adapter("alpha-client", "Alpha client", IntegrationKind.DownloadClient),
            Adapter("alpha-library", "Alpha library", IntegrationKind.MediaLibrary),
        ];

        //Act
        var typeIds = new IntegrationAdapterRegistry(adapters).Descriptors.Select(descriptor => descriptor.TypeId).ToList();

        //Assert
        Assert.Equal(new[] { "alpha-library", "beta-library", "search", "alpha-client", "zeta-client" }, typeIds);
    }

    [Fact]
    public void Find_returns_the_adapter_of_a_type()
    {
        //Arrange
        var library = Adapter("library", "Library", IntegrationKind.MediaLibrary);
        var client = Adapter("client", "Client", IntegrationKind.DownloadClient);
        var registry = new IntegrationAdapterRegistry([library, client]);

        //Act
        var found = registry.Find("client");

        //Assert
        Assert.Same(client, found);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("Library")]
    [InlineData("library ")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_of_an_unknown_or_blank_type_is_null(string? typeId)
    {
        //Arrange
        var registry = new IntegrationAdapterRegistry([Adapter("library", "Library", IntegrationKind.MediaLibrary)]);

        //Act
        var found = registry.Find(typeId);
        var foundContract = registry.Find<IMediaLibraryProvider>(typeId);

        //Assert
        Assert.Null(found);
        Assert.Null(foundContract);
    }

    [Fact]
    public void Find_of_a_contract_returns_the_adapter_only_when_it_implements_it()
    {
        //Arrange
        var library = new LibraryAdapter(Descriptor("library", "Library", IntegrationKind.MediaLibrary));
        var plain = Adapter("plain", "Plain", IntegrationKind.MediaLibrary);
        var registry = new IntegrationAdapterRegistry([library, plain]);

        //Act
        var libraryAsLibrary = registry.Find<IMediaLibraryProvider>("library");
        var libraryAsClient = registry.Find<IDownloadClient>("library");
        var plainAsLibrary = registry.Find<IMediaLibraryProvider>("plain");

        //Assert
        Assert.Same(library, libraryAsLibrary);
        Assert.Null(libraryAsClient);
        Assert.Null(plainAsLibrary);
    }

    [Fact]
    public void Find_of_the_optional_item_identifier_returns_only_a_library_that_implements_it()
    {
        //Arrange
        // MAM-34 — a further interface of the library's class (ADR 0005); a library without it keeps its links in the
        // app, so the registry answers null rather than a stub.
        var identifying = new IdentifyingLibraryAdapter(Descriptor("identifying", "Identifying library", IntegrationKind.MediaLibrary));
        var reading = new LibraryAdapter(Descriptor("reading", "Reading library", IntegrationKind.MediaLibrary));
        var registry = new IntegrationAdapterRegistry([identifying, reading]);

        //Act
        var identifyingAsIdentifier = registry.Find<IMediaLibraryItemIdentifier>("identifying");
        var identifyingAsLibrary = registry.Find<IMediaLibraryProvider>("identifying");
        var readingAsIdentifier = registry.Find<IMediaLibraryItemIdentifier>("reading");

        //Assert
        Assert.Same(identifying, identifyingAsIdentifier);
        Assert.Same(identifying, identifyingAsLibrary);
        Assert.Null(readingAsIdentifier);
    }

    [Fact]
    public void Construction_with_a_duplicate_type_id_throws_naming_it()
    {
        //Arrange
        IIntegrationAdapter[] adapters =
        [
            Adapter("library", "Library", IntegrationKind.MediaLibrary),
            Adapter("library", "Another library", IntegrationKind.MediaLibrary),
        ];

        //Act
        var exception = Record.Exception(() => new IntegrationAdapterRegistry(adapters));

        //Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Two integration adapters are registered as “library”.", invalid.Message);
    }

    [Theory]
    [InlineData("Library")]
    [InlineData("l")]
    [InlineData("1library")]
    [InlineData("-library")]
    [InlineData("media library")]
    [InlineData("media_library")]
    [InlineData("library\n")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    [InlineData("")]
    public void Construction_with_a_malformed_type_id_throws_naming_it(string typeId)
    {
        //Arrange
        IIntegrationAdapter[] adapters = [Adapter(typeId, "Library", IntegrationKind.MediaLibrary)];

        //Act
        var exception = Record.Exception(() => new IntegrationAdapterRegistry(adapters));

        //Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal($"“{typeId}” is not a valid integration adapter type id.", invalid.Message);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("media-library-2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")]
    public void Construction_accepts_a_well_formed_type_id(string typeId)
    {
        //Arrange
        IIntegrationAdapter[] adapters = [Adapter(typeId, "Library", IntegrationKind.MediaLibrary)];

        //Act
        var registry = new IntegrationAdapterRegistry(adapters);

        //Assert
        Assert.Equal(typeId, Assert.Single(registry.Descriptors).TypeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Construction_with_a_blank_display_name_throws(string? displayName)
    {
        //Arrange
        IIntegrationAdapter[] adapters = [Adapter("library", displayName!, IntegrationKind.MediaLibrary)];

        //Act
        var exception = Record.Exception(() => new IntegrationAdapterRegistry(adapters));

        //Assert
        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("The integration adapter “library” has no display name.", invalid.Message);
    }

    [Fact]
    public void An_empty_registry_has_no_descriptors_and_finds_nothing()
    {
        //Arrange
        var registry = new IntegrationAdapterRegistry([]);

        //Act
        var found = registry.Find("library");

        //Assert
        Assert.Empty(registry.Descriptors);
        Assert.Null(found);
    }

    private static IntegrationAdapterDescriptor Descriptor(string typeId, string displayName, IntegrationKind kind) =>
        new(typeId, displayName, kind,
            CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Optional, ReportsFilePaths: true);

    private static IIntegrationAdapter Adapter(string typeId, string displayName, IntegrationKind kind) =>
        new PlainAdapter(Descriptor(typeId, displayName, kind));

    /// <summary>A type that serves no kind contract yet — every real adapter of MAM-7.</summary>
    private sealed class PlainAdapter(IntegrationAdapterDescriptor descriptor) : IIntegrationAdapter
    {
        public IntegrationAdapterDescriptor Descriptor => descriptor;

        public Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionTestResult.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));
    }

    /// <summary>A type that also serves the library contract, as JellyfinAdapter does since MAM-8.</summary>
    private sealed class LibraryAdapter(IntegrationAdapterDescriptor descriptor) : IIntegrationAdapter, IMediaLibraryProvider
    {
        public IntegrationAdapterDescriptor Descriptor => descriptor;

        public MediaLibraryCapabilities Capabilities { get; } = new(GroupsVersionsUnderOneItem: false);

        public Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionTestResult.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));

        public Task<IntegrationCallResult<LibraryItemPage>> ListItemsAsync(IntegrationConnectionSettings settings, LibraryPageRequest page, CancellationToken cancellationToken = default) =>
            Task.FromResult(IntegrationCallResult<LibraryItemPage>.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));

        public Task<IntegrationCallResult<IReadOnlyList<LibraryLocation>>> ListLocationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(IntegrationCallResult<IReadOnlyList<LibraryLocation>>.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));
    }

    /// <summary>A library type that also identifies its items, as JellyfinAdapter does since MAM-34.</summary>
    private sealed class IdentifyingLibraryAdapter(IntegrationAdapterDescriptor descriptor)
        : IIntegrationAdapter, IMediaLibraryProvider, IMediaLibraryItemIdentifier
    {
        public IntegrationAdapterDescriptor Descriptor => descriptor;

        public MediaLibraryCapabilities Capabilities { get; } = new(GroupsVersionsUnderOneItem: false);

        public Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectionTestResult.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));

        public Task<IntegrationCallResult<LibraryItemPage>> ListItemsAsync(IntegrationConnectionSettings settings, LibraryPageRequest page, CancellationToken cancellationToken = default) =>
            Task.FromResult(IntegrationCallResult<LibraryItemPage>.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));

        public Task<IntegrationCallResult<IReadOnlyList<LibraryLocation>>> ListLocationsAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(IntegrationCallResult<IReadOnlyList<LibraryLocation>>.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));

        public IReadOnlyList<MediaWorkKind> IdentifiableKinds { get; } = [MediaWorkKind.Movie];

        public string UnidentifiableKindReason(MediaWorkKind kind) => "The test adapter identifies films only.";

        public Task<IntegrationCallResult<LibraryIdentification>> IdentifyAsync(
            IntegrationConnectionSettings settings, LibraryIdentifyRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(IntegrationCallResult<LibraryIdentification>.Failed(IntegrationCallStatus.Unreachable, "The test adapter is never called."));
    }
}
