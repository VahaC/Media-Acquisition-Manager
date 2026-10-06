namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-34 — what queued a preparation of the TMDb suggestions for library works. Stored by name.</summary>
public enum LibraryLinkSuggestionRunTrigger
{
    /// <summary>A library sync stored a new snapshot: works without a TMDb id may have come or gone.</summary>
    LibrarySynced = 1,

    /// <summary>A metadata catalog was connected, or switched back on.</summary>
    CatalogConnected = 2,

    /// <summary>The installation owner asked for it; every work is searched again, the cache bypassed.</summary>
    Requested = 3,
}
