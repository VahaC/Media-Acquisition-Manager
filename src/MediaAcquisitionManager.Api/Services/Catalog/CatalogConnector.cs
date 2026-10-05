using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <inheritdoc cref="ICatalogConnector"/>
/// <remarks>
/// The adapter is found through the registry by the stored type (Strategy, ADR 0005), and must serve both
/// contracts: a type that only resolved, or only listed, couldn't compute anything. A row of a type this
/// build no longer registers is passed over, as if switched off. Ordered by the normalised name, which is
/// unique, so two catalogs connected at once always give the same one.
/// </remarks>
public sealed class CatalogConnector(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory) : ICatalogConnector
{
    /// <summary>The sentence a computation without a catalog gets: the owner's Recompute now (409) and a run that
    /// found none any more.</summary>
    public const string NoCatalogError = "Connect a metadata catalog under Integrations first.";

    public async Task<bool> HasCatalogAsync(CancellationToken cancellationToken = default) =>
        await FindAsync(cancellationToken) is not null;

    public async Task<CatalogConnectResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (await FindAsync(cancellationToken) is not { } found)
            return CatalogConnectResult.NoCatalog(NoCatalogError);

        var settings = settingsFactory.Create(found.Integration);
        if (settings.Settings is not { } built)
            return CatalogConnectResult.SecretUnreadable(settings.Error!);

        return CatalogConnectResult.Connected(new CatalogConnection(
            found.Integration.Id, found.Integration.Name, built, found.Adapter, found.Catalog, found.Resolver));
    }

    private async Task<Found?> FindAsync(CancellationToken cancellationToken)
    {
        // A handful of rows at most: the kind and switch filter in SQL, the registry in memory.
        var integrations = await db.IntegrationConnections
            .AsNoTracking()
            .Where(integration => integration.Kind == IntegrationKind.Metadata && integration.IsEnabled)
            .OrderBy(integration => integration.NormalizedName)
            .ToListAsync(cancellationToken);
        foreach (var integration in integrations)
        {
            if (adapters.Find(integration.AdapterType) is { } adapter
                && adapters.Find<IMetadataCatalog>(integration.AdapterType) is { } catalog
                && adapters.Find<IMetadataResolver>(integration.AdapterType) is { } resolver)
                return new Found(integration, adapter, catalog, resolver);
        }

        return null;
    }

    private sealed record Found(IntegrationConnectionEntity Integration, IIntegrationAdapter Adapter, IMetadataCatalog Catalog, IMetadataResolver Resolver);
}
