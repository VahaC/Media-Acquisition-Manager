namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — how a request for a preparation of the TMDb suggestions ended.</summary>
public enum LibraryLinkSuggestionRunStartStatus
{
    /// <summary>A run is queued: a new one, or the one already waiting, which the request joined.</summary>
    Queued = 1,

    /// <summary>No metadata catalog is connected, so there is nothing to search; nothing was written.</summary>
    NoCatalog = 2,

    /// <summary>The queue is full; the new run was recorded as failed with the error.</summary>
    Unavailable = 3,
}
