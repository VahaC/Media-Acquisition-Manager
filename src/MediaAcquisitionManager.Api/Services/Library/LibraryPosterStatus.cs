namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>MAM-35 — how a request for a library work's poster ended.</summary>
public enum LibraryPosterStatus
{
    /// <summary>The poster's bytes, from the cache or just fetched from the library.</summary>
    Served = 1,

    /// <summary>
    /// Nothing to serve: a name that is no poster's, an item and tag no library reported, a poster the library doesn't
    /// have (any more), or one not cached while its library is switched off, removed or of a type this build doesn't
    /// read. Answered as a missing row is, without saying which.
    /// </summary>
    NotFound = 2,

    /// <summary>The library couldn't be asked for it, or failed to send it; the error says why.</summary>
    Failed = 3,
}
