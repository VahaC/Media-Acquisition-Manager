namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>How a request to start a library sync ended.</summary>
public enum LibrarySyncStartStatus
{
    /// <summary>The run was recorded and queued; the worker takes it from here.</summary>
    Queued = 1,

    /// <summary>No media library integration has that id; nothing was written.</summary>
    NotFound = 2,

    /// <summary>It can't be synced now — switched off, a type this version can't read a library from,
    /// or a sync of it already under way — which the error says; nothing was written.</summary>
    Conflict = 3,

    /// <summary>Too many syncs are waiting; the run was recorded as failed with the error.</summary>
    Unavailable = 4,
}
