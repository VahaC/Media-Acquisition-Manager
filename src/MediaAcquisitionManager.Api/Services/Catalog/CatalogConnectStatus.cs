namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>MAM-33 — how finding the metadata catalog to read from went.</summary>
public enum CatalogConnectStatus
{
    /// <summary>A catalog is connected, its settings built; nothing has been sent to it.</summary>
    Connected = 1,

    /// <summary>No enabled metadata integration of a type this build serves as a catalog exists.</summary>
    NoCatalog = 2,

    /// <summary>The catalog's stored key can't be decrypted; the error says how to fix it.</summary>
    SecretUnreadable = 3,
}
