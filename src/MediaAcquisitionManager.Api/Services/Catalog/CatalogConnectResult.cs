namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>MAM-33 — the outcome of <see cref="ICatalogConnector.ConnectAsync"/>.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Connection">The catalog when <see cref="CatalogConnectStatus.Connected"/>.</param>
/// <param name="Error">The sentence for the owner otherwise.</param>
public sealed record CatalogConnectResult(CatalogConnectStatus Status, CatalogConnection? Connection, string? Error)
{
    public static CatalogConnectResult Connected(CatalogConnection connection) => new(CatalogConnectStatus.Connected, connection, null);

    public static CatalogConnectResult NoCatalog(string error) => new(CatalogConnectStatus.NoCatalog, null, error);

    public static CatalogConnectResult SecretUnreadable(string error) => new(CatalogConnectStatus.SecretUnreadable, null, error);
}
