using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Jellyfin;
using MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// The real integration adapters, registered exactly as the app registers them
/// (<c>AddIntegrationsInfrastructure</c>), without the rest of the host — for the adapter tests of
/// Infrastructure.Tests.
/// </summary>
/// <remarks>
/// Only the network is replaced: each named client's primary handler becomes a
/// <see cref="RecordingHttpHandler"/>, so the app's own client configuration (timeout, buffer limit,
/// user agent, no logging handlers) still runs. Every log entry at any level is kept in
/// <see cref="Logs"/>. The adapter classes are internal, so a test reaches them by type id.
/// </remarks>
public sealed class IntegrationAdapterHarness : IDisposable
{
    private static readonly string[] ClientNames =
        [JellyfinAdapter.HttpClientName, TorznabAdapter.HttpClientName, QBittorrentAdapter.HttpClientName];

    private readonly ServiceProvider _services;

    /// <param name="fakeNetwork">False to keep the real <c>SocketsHttpHandler</c>, for a test of what
    /// the real network does (nothing listening, say).</param>
    /// <param name="timeout">A client timeout in place of the app's 15 seconds, so a timeout test takes
    /// milliseconds.</param>
    public IntegrationAdapterHarness(bool fakeNetwork = true, TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        services.AddIntegrationsInfrastructure();

        // Registered after the app's own configuration, so these actions run after it and win.
        if (fakeNetwork)
        {
            UsePrimaryHandler(services, JellyfinAdapter.HttpClientName, JellyfinHttp);
            UsePrimaryHandler(services, TorznabAdapter.HttpClientName, TorznabHttp);
            UsePrimaryHandler(services, QBittorrentAdapter.HttpClientName, QBittorrentHttp);
        }

        if (timeout is { } clientTimeout)
        {
            foreach (var name in ClientNames)
                services.Configure<HttpClientFactoryOptions>(name, options => options.HttpClientActions.Add(client => client.Timeout = clientTimeout));
        }

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>The network behind the <c>jellyfin</c> client.</summary>
    public RecordingHttpHandler JellyfinHttp { get; } = new();

    /// <summary>The network behind the <c>torznab</c> client.</summary>
    public RecordingHttpHandler TorznabHttp { get; } = new();

    /// <summary>The network behind the <c>qbittorrent</c> client.</summary>
    public RecordingHttpHandler QBittorrentHttp { get; } = new();

    /// <summary>Every entry logged, at every level, by any category.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>The registered adapter of <paramref name="typeId"/>, found the way the app finds it.</summary>
    public IIntegrationAdapter Adapter(string typeId) =>
        _services.GetRequiredService<IIntegrationAdapterRegistry>().Find(typeId)
        ?? throw new InvalidOperationException($"No integration adapter is registered as “{typeId}”.");

    public void Dispose() => _services.Dispose();

    private static void UsePrimaryHandler(IServiceCollection services, string name, RecordingHttpHandler handler) =>
        services.Configure<HttpClientFactoryOptions>(
            name, options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = handler));
}
