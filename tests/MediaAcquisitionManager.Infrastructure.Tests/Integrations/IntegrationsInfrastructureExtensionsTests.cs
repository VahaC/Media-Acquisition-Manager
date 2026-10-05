using System.Net;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
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
    public static TheoryData<string> ClientNames => ["jellyfin", "torznab", "qbittorrent", "tmdb"];

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
    [InlineData("tmdb")]
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
    public void The_registry_lists_jellyfin_torznab_qbittorrent_and_tmdb_once_each_with_their_descriptors()
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
                new IntegrationAdapterDescriptor(
                    "tmdb", "TMDb", IntegrationKind.Metadata,
                    CredentialRequirement.NotUsed, CredentialRequirement.NotUsed, CredentialRequirement.Required, ReportsFilePaths: false),
            ],
            registry.Descriptors);
        Assert.Equal(4, adapters.Count);
        Assert.IsType<IntegrationAdapterRegistry>(registry);
        // Singletons: the registry hands out the very instances the container holds.
        Assert.Same(Assert.Single(adapters.OfType<JellyfinAdapter>()), registry.Find("jellyfin"));
        Assert.Same(Assert.Single(adapters.OfType<TorznabAdapter>()), registry.Find("torznab"));
        Assert.Same(Assert.Single(adapters.OfType<QBittorrentAdapter>()), registry.Find("qbittorrent"));
        Assert.Same(Assert.Single(adapters.OfType<TmdbAdapter>()), registry.Find("tmdb"));
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
        var tmdb = registry.Find<IMediaLibraryProvider>("tmdb");

        //Assert
        // MAM-8 — the same instance the connection test uses: no second registration to forget.
        Assert.Same(registry.Find("jellyfin"), jellyfin);
        Assert.Null(torznab);
        Assert.Null(qBittorrent);
        // MAM-33 — a catalog is never a source of the library's own facts.
        Assert.Null(tmdb);
    }

    [Fact]
    public void The_registry_finds_torznab_and_nothing_else_as_a_release_search_source()
    {
        //Arrange
        using var services = BuildServices();
        var registry = services.GetRequiredService<IIntegrationAdapterRegistry>();

        //Act
        var torznab = registry.Find<IReleaseSearchProvider>("torznab");
        var jellyfin = registry.Find<IReleaseSearchProvider>("jellyfin");
        var qBittorrent = registry.Find<IReleaseSearchProvider>("qbittorrent");
        var tmdb = registry.Find<IReleaseSearchProvider>("tmdb");

        //Assert
        // MAM-10 — the same instance the connection test uses: no second registration to forget.
        Assert.Same(registry.Find("torznab"), torznab);
        Assert.Null(jellyfin);
        Assert.Null(qBittorrent);
        Assert.Null(tmdb);
    }

    [Fact]
    public void The_registry_finds_qbittorrent_and_nothing_else_as_a_download_client()
    {
        //Arrange
        using var services = BuildServices();
        var registry = services.GetRequiredService<IIntegrationAdapterRegistry>();

        //Act
        var qBittorrent = registry.Find<IDownloadClient>("qbittorrent");
        var jellyfin = registry.Find<IDownloadClient>("jellyfin");
        var torznab = registry.Find<IDownloadClient>("torznab");
        var tmdb = registry.Find<IDownloadClient>("tmdb");

        //Assert
        // MAM-11 — the same instance the connection test uses: no second registration to forget, and one
        // set of kept sessions.
        Assert.Same(registry.Find("qbittorrent"), qBittorrent);
        Assert.Null(jellyfin);
        Assert.Null(torznab);
        Assert.Null(tmdb);
    }

    [Fact]
    public void The_registry_finds_tmdb_and_nothing_else_as_a_metadata_resolver_and_catalog()
    {
        //Arrange
        using var services = BuildServices();
        var registry = services.GetRequiredService<IIntegrationAdapterRegistry>();
        string[] others = ["jellyfin", "torznab", "qbittorrent"];

        //Act
        var resolver = registry.Find<IMetadataResolver>("tmdb");
        var catalog = registry.Find<IMetadataCatalog>("tmdb");
        var otherResolvers = others.Select(typeId => registry.Find<IMetadataResolver>(typeId)).ToList();
        var otherCatalogs = others.Select(typeId => registry.Find<IMetadataCatalog>(typeId)).ToList();

        //Assert
        // MAM-33 — the same instance the connection test uses: no second registration to forget, and one
        // pacing gate for every call.
        Assert.Same(registry.Find("tmdb"), resolver);
        Assert.Same(registry.Find("tmdb"), catalog);
        Assert.All(otherResolvers, Assert.Null);
        Assert.All(otherCatalogs, Assert.Null);
    }

    [Fact]
    public async Task A_destination_listing_logs_nothing_from_the_http_client()
    {
        //Arrange
        // Every level recorded; qBittorrent answers, so the listing signs in and reads everything it reads.
        using var harness = new IntegrationAdapterHarness();
        var settings = new IntegrationConnectionSettings(Guid.NewGuid(), "http://service.test:8080", "admin", "correct-horse-battery", null);
        harness.QBittorrentHttp.Handler = (request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v2/auth/login" => VendorResponses.WithSetCookie(
                new HttpResponseMessage(HttpStatusCode.NoContent), "QBT_SID_8080=Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g; path=/"),
            "/api/v2/app/version" => VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain; charset=UTF-8"),
            "/api/v2/torrents/categories" => VendorResponses.Sample("QBittorrent/categories.json", "application/json"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var client = harness.Adapter<IDownloadClient>("qbittorrent");

        //Act
        var destinations = await client.ListDestinationsAsync(settings, TestContext.Current.CancellationToken);

        //Assert
        Assert.True(destinations.IsSuccess);
        Assert.Equal(3, harness.QBittorrentHttp.Requests.Count);
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains("correct-horse-battery", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains("Zq3xY9LmN2pQ7rT4vW8sK1aB6cD0eF5g", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_submission_and_a_job_lookup_log_nothing_from_the_http_client()
    {
        //Arrange
        // Every level recorded; qBittorrent adds the magnet and lists it, so each call sends everything it sends.
        using var harness = new IntegrationAdapterHarness();
        var qBittorrent = new FakeQBittorrentServer("http://service.test:8080");
        qBittorrent.Arm(harness.QBittorrentHttp);
        var settings = new IntegrationConnectionSettings(
            Guid.NewGuid(), qBittorrent.BaseUrl, FakeQBittorrentServer.DefaultUserName, FakeQBittorrentServer.DefaultPassword, null);
        const string magnet = "magnet:?xt=urn:btih:4a3c1931c85c9c568c39a8eb947ed382824b1cdd&tr=http%3A%2F%2Fbt.toloka.test%2Fa1b2c3d4e5f6a7b8%2Fannounce";
        var client = harness.Adapter<IDownloadClient>("qbittorrent");
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var submission = await client.SubmitAsync(
            settings, new DownloadSubmission(new ReleasePayload(ReleasePayloadKind.Magnet, null, magnet), "Movies", null), cancellationToken);
        var jobs = await client.FindJobsAsync(settings, ["4a3c1931c85c9c568c39a8eb947ed382824b1cdd"], cancellationToken);

        //Assert
        Assert.True(submission.IsSuccess);
        Assert.Single(jobs.Value!);
        Assert.Equal(5, harness.QBittorrentHttp.Requests.Count);
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains(FakeQBittorrentServer.DefaultPassword, StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains("a1b2c3d4e5f6a7b8", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_release_search_and_a_payload_fetch_log_nothing_from_the_http_client()
    {
        //Arrange
        // Every level recorded; the feed answers, so each call reads what it got back.
        using var harness = new IntegrationAdapterHarness();
        var settings = new IntegrationConnectionSettings(null, "http://service.test:9696/1/api", null, null, AnyApiKey);
        new FakeTorznabServer(settings.BaseUrl, AnyApiKey).Arm(harness.TorznabHttp);
        var search = harness.Adapter<IReleaseSearchProvider>("torznab");
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var capabilities = await search.GetCapabilitiesAsync(settings, cancellationToken);
        var hits = await search.SearchAsync(settings, new ReleaseSearchQuery(ReleaseSearchMode.General, "Interstellar", null, [], [], 100), cancellationToken);
        var payload = await search.FetchPayloadAsync(settings, hits.Value![0].Locator, cancellationToken);

        //Assert
        Assert.True(capabilities.IsSuccess);
        Assert.True(payload.IsSuccess);
        Assert.Equal(3, harness.TorznabHttp.Requests.Count);
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains(AnyApiKey, StringComparison.Ordinal));
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
        await library.ListItemsAsync(settings, new LibraryPageRequest(MediaWorkKind.Movie, 0, 50), cancellationToken);

        //Assert
        Assert.Equal(2, harness.JellyfinHttp.Requests.Count);
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains(AnyApiKey, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_metadata_call_and_a_poster_fetch_log_nothing_from_the_http_client()
    {
        //Arrange
        // Every level recorded; TMDb answers, so each call reads what it got back.
        using var harness = new IntegrationAdapterHarness();
        var tmdb = FakeTmdbServer.Arm(harness.TmdbHttp);
        tmdb.AddWork(new FakeTmdbWork(MediaWorkKind.Movie, 679, "Чужі", "Aliens", "1986-07-18", PosterPath: "/aliens.jpg"));
        var settings = new IntegrationConnectionSettings(null, FakeTmdbServer.BaseUrl, null, null, FakeTmdbServer.Token);
        var catalog = harness.Adapter<IMetadataCatalog>("tmdb");
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var work = await catalog.GetWorkAsync(settings, MediaWorkKind.Movie, "679", cancellationToken);
        var poster = await catalog.GetPosterAsync(settings, work.Value!.Summary.PosterPath!, cancellationToken);

        //Assert
        Assert.True(poster.IsSuccess);
        Assert.Equal(2, harness.TmdbHttp.Requests.Count);
        Assert.DoesNotContain(
            harness.Logs.Entries,
            entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Message.Contains(FakeTmdbServer.Token, StringComparison.Ordinal));
    }

    // The app registers the clock in Program (TryAddSingletonTimeProvider); MAM-33 — the TMDb adapter's
    // pacing takes it.
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddSingleton(TimeProvider.System)
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
