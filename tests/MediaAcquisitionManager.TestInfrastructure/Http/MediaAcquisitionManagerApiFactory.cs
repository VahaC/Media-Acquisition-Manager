using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaAcquisitionManager.Api;
using MediaAcquisitionManager.Api.Serialization;
using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.Infrastructure.Security;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using MediaAcquisitionManager.TestInfrastructure.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
/// Only seams to the outside world are replaced — today that is the clock; later the vendor
/// clients. The app's own hosted services are removed, so a background loop never races a test;
/// tests drive that logic directly instead. Subclass to flip behaviour (see
/// <see cref="FirstRunApiFactory"/>, <see cref="PipelineProbeApiFactory"/>).
/// </para>
/// </remarks>
public class MediaAcquisitionManagerApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The fixed <c>Encryption:Key</c> the host runs with (32 bytes, base64).</summary>
    public const string TestEncryptionKey = "+UksGT7fnK0A27M5bfh88AC9htpg4Bzj4XUqiL16ceo=";

    /// <summary>The fixed <c>Jwt:Secret</c> the host runs with (64 bytes, base64).</summary>
    public const string TestJwtSecret =
        "o2WgVHs0skFjSqlIC1nhOoFK78306PBQmljIUxDq/v3uZXmTAxcZ3tRxz+W8ND5OsUlB8u5LPJHvduMqtio63g==";

    private IServiceScope? _scope;
    private HttpClient? _client;

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
    // nothing (a token, a header) carries over from one test to the next.
    private HttpClient Client =>
        _client ??= CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── HTTP helpers ───────────────────────────────────────────────────────────────────────────

    public Task<HttpResponseMessage> GetHttpRequest(string url, string? token = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Get, url, token, body: null));

    public Task<HttpResponseMessage> PostHttpRequest(string url, string? token = null, object? body = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Post, url, token, body));

    public Task<HttpResponseMessage> PutHttpRequest(string url, string? token = null, object? body = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Put, url, token, body));

    public Task<HttpResponseMessage> PatchHttpRequest(string url, string? token = null, object? body = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Patch, url, token, body));

    public Task<HttpResponseMessage> DeleteHttpRequest(string url, string? token = null, object? body = null) =>
        SendHttpRequest(CreateRequest(HttpMethod.Delete, url, token, body));

    /// <summary>Sends a hand-built request — for the rare test that needs a header the helpers
    /// above don't set (a <c>traceparent</c>, say).</summary>
    public Task<HttpResponseMessage> SendHttpRequest(HttpRequestMessage request) => Client.SendAsync(request);

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
    /// clock, empties <see cref="Logs"/> and renews <see cref="Scope"/>. Model-driven: a new entity
    /// needs no line here.
    /// </summary>
    public async Task ResetAsync()
    {
        _scope?.Dispose();
        _scope = null;
        Time.Reset();
        Logs.Clear();

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
            // Added beside the default providers, not instead of them: a failing run still shows
            // the host's log in the test output.
            services.AddSingleton<ILoggerProvider>(Logs);
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

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? token, object? body)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        return request;
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
