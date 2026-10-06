namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — how a change to a library work's TMDb links, or a read of them, went.</summary>
public enum LibraryLinkStatus
{
    /// <summary>Done; the result carries what the work's dialog shows now (none for a link removed by its id).</summary>
    Succeeded = 1,

    /// <summary>No such work or link: a bare 404, saying nothing about what exists.</summary>
    NotFound = 2,

    /// <summary>The request breaks a rule; the error says which (400).</summary>
    Invalid = 3,

    /// <summary>The catalog has no film or show of that id; the error says so (404 with the sentence, R18).</summary>
    CatalogWorkNotFound = 4,

    /// <summary>The state forbids it now — no catalog, no library item, a write under way, a library switched off; the
    /// error says what to do (409).</summary>
    Conflict = 5,

    /// <summary>The catalog or the library couldn't be read or changed; the error says why, its secrets masked (502).</summary>
    BadGateway = 6,

    /// <summary>The work has no link to remove (204).</summary>
    NothingToRemove = 7,
}
