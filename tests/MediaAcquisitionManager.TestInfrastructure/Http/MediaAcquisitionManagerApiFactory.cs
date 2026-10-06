using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaAcquisitionManager.Api;
using MediaAcquisitionManager.Api.Serialization;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Api.Services.LibraryLinks;
using MediaAcquisitionManager.Api.Services.Recommendations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using MediaAcquisitionManager.TestInfrastructure.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// Boots the real API in memory — routing, model binding, filters, status mapping, serialization,
/// migrations and a real SQLite database — so tests fire actual HTTP requests at real URLs.
/// </summary>
/// <remarks>
/// <para>
/// Each instance (one per test class, as an <c>IClassFixture</c>) owns a directory under
/// <see cref="TestDatabase.ProcessDirectory"/> that serves as the app's data directory: a copy of
/// the process-wide migrated template as <c>mam.db</c>, and a <c>wwwroot</c> holding the repository's
/// <c>frontend/index.html</c> as the web root, so the SPA fallback is exercised against the real shell.
/// The directory goes when the factory is disposed.
/// </para>
/// <para>
/// Only seams to the outside world are replaced — the clock, the vendor HTTP and the host's files: each
/// integration client's primary handler is a <see cref="RecordingHttpHandler"/> (<see cref="JellyfinHttp"/>,
/// <see cref="TorznabHttp"/>, <see cref="QBittorrentHttp"/>, <see cref="TmdbHttp"/> — TMDb's image host
/// included, MAM-33), so the real adapters run with the app's own client configuration and only the
/// network is faked; and the file inventory, the Core seam to the app's mounts and the media inspector,
/// is <see cref="Files"/> (MAM-9). One more adapter type,
/// <see cref="FakeAdapter"/>, is registered beside the real ones for tests about what the API does with
/// an adapter's answer. Two more stand-ins keep the suite fast and its tests independent without
/// faking any behaviour: a password hasher with a cheap iteration count (the real algorithm and format, which
/// records what it verifies — see <see cref="PasswordHasher"/>), and a synthetic client address per
/// request (see <see cref="PinClientAddress"/>). One save interceptor, <see cref="SaveChangesHook"/>,
/// one transaction interceptor, <see cref="TransactionHook"/>, and one command interceptor,
/// <see cref="DbCommandHook"/>, do nothing until a test arms them to stage a race. The app's own hosted
/// services are removed, so a background loop never races a test; tests drive that logic directly
/// instead — the library sync, file inspection, folder scan and recommendation run workers and reconcilers, and
/// MAM-34's links' background writer and TMDb suggestion runs with their reconcilers, through their singletons
/// (<see cref="Resolve{T}"/>).
/// Subclass to flip behaviour (see <see cref="FirstRunApiFactory"/>,
/// <see cref="PipelineProbeApiFactory"/>).
/// </para>
/// </remarks>
public class MediaAcquisitionManagerApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The fixed <c>Encryption:Key</c> the host runs with (32 bytes, base64).</summary>
    public const string TestEncryptionKey = "+UksGT7fnK0A27M5bfh88AC9htpg4Bzj4XUqiL16ceo=";

    /// <summary>The fixed <c>Jwt:Secret</c> the host runs with (64 bytes, base64).</summary>
    public const string TestJwtSecret =
        "o2WgVHs0skFjSqlIC1nhOoFK78306PBQmljIUxDq/v3uZXmTAxcZ3tRxz+W8ND5OsUlB8u5LPJHvduMqtio63g==";

    /// <summary>
    /// The PBKDF2 iteration count of the host's hasher. The production count costs a few hundred
    /// milliseconds per hash, and a suite that seeds users and signs in hundreds of times would spend
    /// minutes on nothing but that; the algorithm and the format are the real ones.
    /// </summary>
    public const int TestPasswordHashIterations = 1_000;

    private IServiceScope? _scope;
    private HttpClient? _client;
    private int _clientAddressCount;
    private IPAddress? _pinnedClientAddress;

    public MediaAcquisitionManagerApiFactory()
    {
        DataDirectory = TestDatabase.CreateFactoryDirectory();
        WebRootDirectory = Path.Combine(DataDirectory, "wwwroot");
    }

    /// <summary>
    /// The JSON options the tests read and write bodies with: web casing and enums as strings,
    /// mirroring what the API's controllers use.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new UtcDateTimeConverter() },
    };

    /// <summary>The app's data directory (<c>Storage:DataDirectory</c>) for this factory.</summary>
    public string DataDirectory { get; }

    /// <summary>The app's web root for this factory; it holds a copy of <c>frontend/index.html</c>.</summary>
    public string WebRootDirectory { get; }

    /// <summary>The clock the host runs on. Rewound by <see cref="ResetAsync"/>.</summary>
    public TestTimeProvider Time { get; } = new();

    /// <summary>Every entry the host logs, startup included. Emptied by <see cref="ResetAsync"/>.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>
    /// The host's password hasher, at <see cref="TestPasswordHashIterations"/>: every hash it verifies
    /// is recorded, and a test can run code inside the next verification. Emptied by
    /// <see cref="ResetAsync"/>.
    /// </summary>
    public RecordingPasswordHasher PasswordHasher { get; } = new(TestPasswordHashIterations);

    /// <summary>The <c>fake</c> integration type, registered beside the real ones. Reset by
    /// <see cref="ResetAsync"/>.</summary>
    public FakeIntegrationAdapter FakeAdapter { get; } = new();

    /// <summary>MAM-34 — the <c>fake-library</c> integration type, registered beside the real ones: a media library that
    /// identifies items and answers each identification as a test arms it, so the link writer and its worker are tested
    /// without a Jellyfin. Reset by <see cref="ResetAsync"/>.</summary>
    public FakeLibraryItemIdentifier FakeLibraryIdentifier { get; } = new();

    /// <summary>The network behind the Jellyfin adapter's client; 503 until armed. Reset by
    /// <see cref="ResetAsync"/>.</summary>
    public RecordingHttpHandler JellyfinHttp { get; } = new();

    /// <summary>The network behind the Torznab adapter's client; 503 until armed. Reset by
    /// <see cref="ResetAsync"/>.</summary>
    public RecordingHttpHandler TorznabHttp { get; } = new();

    /// <summary>The network behind the qBittorrent adapter's client; 503 until armed. Reset by
    /// <see cref="ResetAsync"/>.</summary>
    public RecordingHttpHandler QBittorrentHttp { get; } = new();

    /// <summary>MAM-33 — the network behind the TMDb adapter's client, its API and its image host alike;
    /// 503 until armed (<c>FakeTmdbServer.Arm</c>). Reset by <see cref="ResetAsync"/>.</summary>
    public RecordingHttpHandler TmdbHttp { get; } = new();

    /// <summary>A save interceptor on every DbContext of the host that runs a test's code before a chosen
    /// save; does nothing until armed. Disarmed by <see cref="ResetAsync"/>.</summary>
    public SaveChangesHook SaveChangesHook { get; } = new();

    /// <summary>A transaction interceptor on every DbContext of the host that runs a test's code before a
    /// chosen transaction begins (MAM-11); does nothing until armed. Disarmed by <see cref="ResetAsync"/>.</summary>
    public TransactionHook TransactionHook { get; } = new();

    /// <summary>A command interceptor on every DbContext of the host that runs a test's code before a chosen
    /// command is sent (MAM-12); does nothing until armed. Disarmed by <see cref="ResetAsync"/>.</summary>
    public DbCommandHook DbCommandHook { get; } = new();

    /// <summary>The app's mounts and media inspector (MAM-9), and the walk of its media folders (MAM-18);
    /// nothing is anywhere until a test adds a file. Emptied by <see cref="ResetAsync"/>.</summary>
    public FakeFileInventory Files { get; } = new();

    /// <summary>
    /// A DI scope for seeders and assertions, created on first use and renewed by
    /// <see cref="ResetAsync"/>, so a test never sees a DbContext that tracked the previous test's rows.
    /// </summary>
    public IServiceProvider Scope => (_scope ??= Services.CreateScope()).ServiceProvider;

    /// <summary>
    /// Whether the data directory starts with a copy of the migrated template. When false the app
    /// meets an empty directory, like on the first start of a fresh installation.
    /// </summary>
    protected virtual bool StartFromMigratedDatabase => true;

    // One client per factory, never one per test: every request is built as its own message, so
    // nothing (a token, a header) carries over from one test to the next. MAM-4 — for the same
    // reason no cookie jar: with one, the refresh cookie a test's sign-in set would ride along on the
    // next test's requests. A test sends the cookie it means to, through the helpers' cookie argument.
    private HttpClient Client =>
        _client ??= CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    // ── HTTP helpers ───────────────────────────────────────────────────────────────────────────
    // token: the bearer access token, none = anonymous. cookie: the Cookie header, sent verbatim
    // (ResponseCookie.RequestHeader, AuthorizedUser.RefreshCookie).

    public Task<HttpResponseMessage> GetHttpRequest(string url, string? token = null, string? cookie = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Get, url, token, body: null, cookie));

    public Task<HttpResponseMessage> PostHttpRequest(string url, string? token = null, object? body = null, string? cookie = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Post, url, token, body, cookie));

    public Task<HttpResponseMessage> PutHttpRequest(string url, string? token = null, object? body = null, string? cookie = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Put, url, token, body, cookie));

    public Task<HttpResponseMessage> PatchHttpRequest(string url, string? token = null, object? body = null, string? cookie = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Patch, url, token, body, cookie));

    public Task<HttpResponseMessage> DeleteHttpRequest(string url, string? token = null, object? body = null, string? cookie = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Delete, url, token, body, cookie));

    /// <summary>Sends a hand-built request — for the rare test that needs a header the helpers
    /// above don't set (a <c>traceparent</c>, say).</summary>
    public Task<HttpResponseMessage> SendHttpRequest(HttpRequestMessage request) => Client.SendAsync(request);

    // ── Client address ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Makes every following request come from one new address — to trip a per-address rate limit
    /// on purpose — until <see cref="ResetAsync"/> or the next pin. Unpinned, each request comes from
    /// an address of its own, so no test is ever limited by another's requests.
    /// </summary>
    /// <remarks>
    /// Always a fresh address, never one used before: the limiter's counters live in the host, which
    /// outlives <see cref="ResetAsync"/>, so an address an earlier test exhausted would still be
    /// exhausted. Pin again to get "another address" within a test.
    /// </remarks>
    public IPAddress PinClientAddress()
    {
        var address = NewClientAddress();
        Volatile.Write(ref _pinnedClientAddress, address);
        return address;
    }

    // ── DI and database access ─────────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="action"/> with a fresh DbContext in its own scope — the way to
    /// assert persisted rows without the test's tracked entities answering instead of the database.</summary>
    public async Task WithDbAsync(Func<ApplicationDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    /// <summary>A service from the root container — the live singletons (queues, configuration).</summary>
    public T Resolve<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>
    /// Puts the factory back to a clean state; every test calls it first. Empties every table of the
    /// EF model in one batched command (a round trip per table adds up across a suite), rewinds the
    /// clock, empties <see cref="Logs"/> and <see cref="PasswordHasher"/>, disarms and empties
    /// <see cref="FakeAdapter"/>, <see cref="FakeLibraryIdentifier"/> and the vendor handlers, disarms <see cref="SaveChangesHook"/>,
    /// <see cref="TransactionHook"/> and <see cref="DbCommandHook"/>, empties
    /// <see cref="Files"/>, drains the library sync, file inspection, folder scan and — MAM-33 — recommendation run
    /// queues (a run id a test queued but never ran would otherwise reach the next test's worker) and — MAM-34 — the
    /// links' background writer's signal and the TMDb suggestion run queue, unpins the client
    /// address and renews <see cref="Scope"/>.
    /// Model-driven: a new entity needs no line here.
    /// </summary>
    public async Task ResetAsync()
    {
        _scope?.Dispose();
        _scope = null;
        Time.Reset();
        Logs.Clear();
        PasswordHasher.Clear();
        FakeAdapter.Reset();
        FakeLibraryIdentifier.Reset();
        JellyfinHttp.Reset();
        TorznabHttp.Reset();
        QBittorrentHttp.Reset();
        TmdbHttp.Reset();
        SaveChangesHook.Reset();
        TransactionHook.Reset();
        DbCommandHook.Reset();
        Files.Reset();
        var libraryQueue = Services.GetRequiredService<LibrarySyncQueue>();
        while (libraryQueue.Reader.TryRead(out _))
        {
        }

        var inspectionQueue = Services.GetRequiredService<FileInspectionQueue>();
        while (inspectionQueue.Reader.TryRead(out _))
        {
        }

        var scanQueue = Services.GetRequiredService<FolderScanQueue>();
        while (scanQueue.Reader.TryRead(out _))
        {
        }

        // MAM-33 — and the recommendations' runs, queued by a sync, a catalog saved or a request.
        var recommendationQueue = Services.GetRequiredService<RecommendationRunQueue>();
        while (recommendationQueue.Reader.TryRead(out _))
        {
        }

        // MAM-34 — and the links' background writer's signal, given by "Accept all confident" or the reconciler.
        var linkWriteQueue = Services.GetRequiredService<LibraryLinkWriteQueue>();
        while (linkWriteQueue.Reader.TryRead(out _))
        {
        }

        // MAM-34 — and the TMDb suggestions' runs, queued by a sync, a catalog saved or the owner's request.
        var linkSuggestionQueue = Services.GetRequiredService<LibraryLinkSuggestionRunQueue>();
        while (linkSuggestionQueue.Reader.TryRead(out _))
        {
        }

        Volatile.Write(ref _pinnedClientAddress, null);

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Foreign keys off for the batch, so deletion order doesn't matter. The pragma is a no-op
        // inside a transaction, which is why this runs as a plain command.
        var sql = new StringBuilder("PRAGMA foreign_keys = OFF;");
        foreach (var table in db.Model.GetEntityTypes().Select(type => type.GetTableName()).OfType<string>().Distinct())
            sql.Append("DELETE FROM \"").Append(table).Append("\";");
        sql.Append("PRAGMA foreign_keys = ON;");

        await db.Database.ExecuteSqlRawAsync(sql.ToString());
    }

    // ── Host configuration ─────────────────────────────────────────────────────────────────────

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        PrepareDataDirectory();

        // "Testing", not "Development": no user secrets (a developer's real keys never reach a test
        // run, and a user-secrets Jwt:Secret could not mask first-run provisioning), no
        // appsettings.Development.json, and no developer exception page in front of the { error }
        // handler under test.
        builder.UseEnvironment("Testing");
        // The builder turns these on only in Development, so "Testing" alone would build the
        // container unchecked: a captive dependency (a singleton holding the scoped DbContext) would
        // pass the whole suite while `dotnet run` refuses to start. Turned back on, the suite fails
        // the same way Development does.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });

        // With minimal hosting these settings reach Program.Main as command-line arguments, which is
        // what lets them count before builder.Build(): Program resolves the data directory and the
        // first-run secrets from configuration long before the host exists. The web root arrives the
        // same way — SpaFallbackHttpTests proves it by getting this directory's index.html.
        builder.UseSetting($"{StorageOptions.SectionName}:{nameof(StorageOptions.DataDirectory)}", DataDirectory);
        builder.UseWebRoot(WebRootDirectory);
        ConfigureSecrets(builder);

        builder.ConfigureTestServices(services =>
        {
            RemoveAppHostedServices(services);
            ReplaceSingleton<TimeProvider>(services, Time);
            ReplaceSingleton<IPasswordHasher>(services, PasswordHasher);
            // TestServer leaves the remote address empty, which would put every request of the suite
            // into one rate-limit partition, and a sign-in test would be limited by its neighbours.
            services.AddSingleton<IStartupFilter>(new ClientAddressStartupFilter(NextClientAddress));
            // Added beside the default providers, not instead of them: a failing run still shows
            // the host's log in the test output.
            services.AddSingleton<ILoggerProvider>(Logs);
            // Beside the real adapters, not instead of them: the registry takes every one registered.
            services.AddSingleton<IIntegrationAdapter>(FakeAdapter);
            // MAM-34 — a library type that identifies items, answering whatever a test arms, beside the real Jellyfin adapter.
            services.AddSingleton<IIntegrationAdapter>(FakeLibraryIdentifier);
            // The app attaches every save interceptor in the container; unarmed, this one does nothing.
            services.AddSingleton<ISaveChangesInterceptor>(SaveChangesHook);
            // MAM-11 — added to the context's options after the app's own configuration, beside its
            // interceptors: the app takes no transaction interceptor from the container. Unarmed, it does
            // nothing.
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(TransactionHook));
            // MAM-12 — the same way, for the same reason: the app takes no command interceptor from the
            // container. Unarmed, it does nothing.
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(DbCommandHook));
            // MAM-9 — instead of the local one: the host's folders and its ffprobe are the outside world
            // here, the way the vendors' networks are.
            ReplaceSingleton<IFileInventory>(services, Files);
            // Registered after the app's own client configuration, so these actions run after its
            // ConfigurePrimaryHttpMessageHandler and win; everything else about each client (timeout,
            // buffer limit, user agent, no logging handlers) stays the app's.
            UsePrimaryHandler(services, JellyfinAdapter.HttpClientName, JellyfinHttp);
            // MAM-34 — Apply goes through a client of its own; one handler for both, so a test sees every Jellyfin request
            // in one ordered list.
            UsePrimaryHandler(services, JellyfinAdapter.IdentifyHttpClientName, JellyfinHttp);
            UsePrimaryHandler(services, TorznabAdapter.HttpClientName, TorznabHttp);
            UsePrimaryHandler(services, QBittorrentAdapter.HttpClientName, QBittorrentHttp);
            UsePrimaryHandler(services, TmdbAdapter.HttpClientName, TmdbHttp);
        });
    }

    /// <summary>
    /// Presets both first-run secrets, so the app provisions nothing and writes no <c>.secrets</c>
    /// files; a subclass that wants provisioning to run overrides this.
    /// </summary>
    protected virtual void ConfigureSecrets(IWebHostBuilder builder)
    {
        builder.UseSetting(FirstRunSecrets.EncryptionKey.ConfigurationKey, TestEncryptionKey);
        builder.UseSetting(FirstRunSecrets.JwtSecret.ConfigurationKey, TestJwtSecret);
    }

    protected static void ReplaceSingleton<T>(IServiceCollection services, T instance) where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(instance);
    }

    private static void UsePrimaryHandler(IServiceCollection services, string clientName, RecordingHttpHandler handler) =>
        services.Configure<HttpClientFactoryOptions>(
            clientName, options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));

    public override async ValueTask DisposeAsync()
    {
        _scope?.Dispose();
        _scope = null;
        await base.DisposeAsync();
        TestDatabase.DeleteDirectory(DataDirectory);
        GC.SuppressFinalize(this);
    }

    // Overwriting on purpose: when the host fails to start, the next test's Services access runs
    // ConfigureWebHost again, and it must then fail with the host's real error, not "file exists".
    private void PrepareDataDirectory()
    {
        if (StartFromMigratedDatabase)
            TestDatabase.CopyMigratedTemplateTo(Path.Combine(DataDirectory, StorageLayout.DatabaseFileName));

        Directory.CreateDirectory(WebRootDirectory);
        File.Copy(RepositoryPaths.FrontendIndexHtml, Path.Combine(WebRootDirectory, "index.html"), overwrite: true);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? token, object? body, string? cookie)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        if (cookie is not null)
            request.Headers.Add("Cookie", cookie);
        return request;
    }

    private IPAddress NextClientAddress() => Volatile.Read(ref _pinnedClientAddress) ?? NewClientAddress();

    // Counted up through 10.0.0.0/8: 16 million addresses before one repeats, far more requests than
    // a factory ever serves.
    private IPAddress NewClientAddress()
    {
        var number = Interlocked.Increment(ref _clientAddressCount);
        return new IPAddress((byte[])[10, (byte)(number >> 16), (byte)(number >> 8), (byte)number]);
    }

    /// <summary>
    /// Removes the hosted services the app itself registers. Only those: the web host runs as an
    /// <see cref="IHostedService"/> too, and removing every registration would leave a host that
    /// never starts serving.
    /// </summary>
    private static void RemoveAppHostedServices(IServiceCollection services)
    {
        var appHostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                && !descriptor.IsKeyedService
                && IsAppType(ImplementationTypeOf(descriptor)))
            .ToList();

        foreach (var descriptor in appHostedServices)
            services.Remove(descriptor);
    }

    // A factory registration's lambda is compiled into the assembly that registered it, which is
    // the best available answer to "whose service is this" when no implementation type is declared.
    private static Type? ImplementationTypeOf(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType
        ?? descriptor.ImplementationInstance?.GetType()
        ?? descriptor.ImplementationFactory?.Method.DeclaringType;

    private static bool IsAppType(Type? type) =>
        type?.Assembly.GetName().Name?.StartsWith("MediaAcquisitionManager.", StringComparison.Ordinal) == true;
}
