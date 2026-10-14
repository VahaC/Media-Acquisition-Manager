using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-30 — the app plus <see cref="FolderClient"/>, a download client type whose destinations are folders (ADR 0039),
/// beside the real adapters: the Api's folder flow — the destinations made of the library folders, the save rules, the
/// send with its label, the overwrite check, the holdings and the following — tested over real HTTP before and beside a
/// real Transmission. The base factory doesn't register it, so no other suite counts it among the adapter types.
/// </summary>
public sealed class FolderClientApiFactory : MediaAcquisitionManagerApiFactory
{
    /// <summary>The <c>fake-folder-client</c> integration type. Disarmed and emptied by <see cref="ResetAsync"/>.</summary>
    public FakeFolderDownloadClient FolderClient { get; } = new();

    /// <summary>The base factory's reset, and <see cref="FolderClient"/>'s; every test of a class on this factory calls it
    /// first.</summary>
    public new async Task ResetAsync()
    {
        FolderClient.Reset();
        await base.ResetAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Beside the real adapters, not instead of them: the registry takes every one registered.
        builder.ConfigureTestServices(services => services.AddSingleton<IIntegrationAdapter>(FolderClient));
    }
}
