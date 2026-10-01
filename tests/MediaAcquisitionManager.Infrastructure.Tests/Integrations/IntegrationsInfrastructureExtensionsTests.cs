using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Logging;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

public sealed class IntegrationsInfrastructureExtensionsTests
{
    private const string AnyApiKey = "4f1d0b9c2e7a4c3b8d6e5f7a9b0c1d2e";

    /// <summary>
    /// The client names written out rather than read from the adapters' constants: a renamed constant
    /// would leave these names with the factory's default, unconfigured client, and the tests fail.
    /// </summary>
    public static TheoryData<string> ClientNames => ["jellyfin", "torznab", "qbittorrent"];

    [Theory]
    [MemberData(nameof(ClientNames))]
    public void Every_integration_client_has_a_primary_handler_that_follows_no_redirect_and_keeps_no_cookies(string clientName)
    {
        //Arrange
        using var services = BuildServices();
        var handlers = services.GetRequiredService<IHttpMessageHandlerFactory>();

        //Act
        var (_, primary) = Chain(handlers.CreateHandler(clientName));

        //Assert
        var sockets = Assert.IsType<SocketsHttpHandler>(primary);
        // ADR 0007: every request carries a credential, and a followed redirect would carry it along.
        Assert.False(sockets.AllowAutoRedirect);
        // Pooled handlers are shared by every connection of the type: a cookie jar would share one
        // qBittorrent's session with another.
        Assert.False(sockets.UseCookies);
    }

    [Theory]
    [MemberData(nameof(ClientNames))]
    public void Every_integration_client_has_its_timeouts_buffer_limit_and_user_agent(string clientName)
    {
        //Arrange
        using var services = BuildServices();

        //Act
        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        var (_, primary) = Chain(services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName));

        //Assert
        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.IsType<SocketsHttpHandler>(primary).ConnectTimeout);
        Assert.Equal(4 * 1024 * 1024, client.MaxResponseContentBufferSize);
        Assert.Equal("MediaAcquisitionManager/1.0 (+integrations)", client.DefaultRequestHeaders.UserAgent.ToString());
        // One client serves every connection of its type, each with its own address.
        Assert.Null(client.BaseAddress);
    }

    [Theory]
    [MemberData(nameof(ClientNames))]
    public void Every_integration_client_has_no_logging_or_retry_handler(string clientName)
    {
        //Arrange
        using var services = BuildServices();
        var handlers = services.GetRequiredService<IHttpMessageHandlerFactory>();

        //Act
        var (delegating, _) = Chain(handlers.CreateHandler(clientName));

        //Assert
        // The factory's logging handlers write every request URI at Information, and Torznab's key
        // travels in the query (ADR 0004).
        Assert.DoesNotContain(delegating, handler => handler is LoggingHttpMessageHandler or LoggingScopeHttpMessageHandler);
        // Nothing between the factory's own lifetime tracker and the network: no retry, no resilience.
        Assert.All(delegating, handler => Assert.Equal("LifetimeTrackingHttpMessageHandler", handler.GetType().Name));
    }

    [Theory]
    [InlineData("jellyfin")]
    [InlineData("torznab")]
    [InlineData("qbittorrent")]
    public async Task A_connection_test_logs_nothing_from_the_http_client(string typeId)
    {
        //Arrange
        // Every level recorded; the unarmed network answers 503.
        using var harness = new IntegrationAdapterHarness();
        var settings = new IntegrationConnectionSettings(null, "http://service.test:8080/1/api", "admin", "correct-horse-battery", AnyApiKey);

        //Act
        await harness.Adapter(typeId).TestConnectionAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains(AnyApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public void The_registry_lists_jellyfin_torznab_and_qbittorrent_once_each_with_their_descriptors()
    {
        //Arrange
        using var services = BuildServices();

        //Act
        var registry = services.GetRequiredService<IIntegrationAdapterRegistry>();
        var adapters = services.GetServices<IIntegrationAdapter>().ToList();

        //Assert
        // ADR 0005: the type ids are stored on every connection row; renaming one orphans them all.
        Assert.Equal(
            [
                new IntegrationAdapterDescriptor(
                    "jellyfin", "Jellyfin", IntegrationKind.MediaLibrary,
                    CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: true),
                new IntegrationAdapterDescriptor(
                    "torznab", "Torznab", IntegrationKind.ReleaseSearch,
                    CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false),
                new IntegrationAdapterDescriptor(
                    "qbittorrent", "qBittorrent", IntegrationKind.DownloadClient,
                    CredentialRequirement.Optional, CredentialRequirement.Optional, CredentialRequirement.NotUsed, ReportsFilePaths: true),
            ],
            registry.Descriptors);
        Assert.Equal(3, adapters.Count);
        Assert.IsType<IntegrationAdapterRegistry>(registry);
        // Singletons: the registry hands out the very instances the container holds.
        Assert.Same(Assert.Single(adapters.OfType<JellyfinAdapter>()), registry.Find("jellyfin"));
        Assert.Same(Assert.Single(adapters.OfType<TorznabAdapter>()), registry.Find("torznab"));
        Assert.Same(Assert.Single(adapters.OfType<QBittorrentAdapter>()), registry.Find("qbittorrent"));
    }

    [Fact]
    public void The_registry_finds_jellyfin_and_nothing_else_as_a_media_library()
    {
        //Arrange
        using var services = BuildServices();
        var registry = services.GetRequiredService<IIntegrationAdapterRegistry>();

        //Act
        var jellyfin = registry.Find<IMediaLibraryProvider>("jellyfin");
        var torznab = registry.Find<IMediaLibraryProvider>("torznab");
        var qBittorrent = registry.Find<IMediaLibraryProvider>("qbittorrent");

        //Assert
        // MAM-8 — the same instance the connection test uses: no second registration to forget.
        Assert.Same(registry.Find("jellyfin"), jellyfin);
        Assert.Null(torznab);
        Assert.Null(qBittorrent);
    }

    [Fact]
    public async Task A_library_call_logs_nothing_from_the_http_client()
    {
        //Arrange
        // Every level recorded; the unarmed network answers 503.
        using var harness = new IntegrationAdapterHarness();
        var library = harness.Adapter<IMediaLibraryProvider>("jellyfin");
        var settings = new IntegrationConnectionSettings(null, "http://service.test:8096", null, null, AnyApiKey);
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        await library.ListLocationsAsync(settings, cancellationToken);
        await library.ListItemsAsync(settings, new LibraryPageRequest(0, 50), cancellationToken);

        //Assert
        Assert.Equal(2, harness.JellyfinHttp.Requests.Count);
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains(AnyApiKey, StringComparison.Ordinal));
    }

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddIntegrationsInfrastructure()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    /// <summary>The delegating handlers from the outside in, and the primary handler at the end.</summary>
    private static (List<DelegatingHandler> Delegating, HttpMessageHandler Primary) Chain(HttpMessageHandler handler)
    {
        var delegating = new List<DelegatingHandler>();
        while (handler is DelegatingHandler outer)
        {
            delegating.Add(outer);
            handler = outer.InnerHandler!;
        }

        return (delegating, handler);
    }
}
