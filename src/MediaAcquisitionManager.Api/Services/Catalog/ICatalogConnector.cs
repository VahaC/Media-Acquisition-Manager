namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>
/// MAM-33 — which metadata catalog the app reads from (Vasyl's decision 1: TMDb, an integration with a key the
/// owner creates): the first enabled integration of the Metadata kind, by name, whose type this build serves as
/// both a catalog and a resolver. Reads the database only; nothing is sent to the catalog.
/// </summary>
public interface ICatalogConnector
{
    /// <summary>Whether a catalog is connected — what the owner's Recompute now and the page's "connect a catalog"
    /// state ask.</summary>
    Task<bool> HasCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The catalog with its settings built. <see cref="CatalogConnectStatus.NoCatalog"/> with
    /// <see cref="CatalogConnector.NoCatalogError"/> when none is connected;
    /// <see cref="CatalogConnectStatus.SecretUnreadable"/> when its key can't be decrypted.
    /// </summary>
    Task<CatalogConnectResult> ConnectAsync(CancellationToken cancellationToken = default);
}
