namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-26 — how an owner's Retry, Restore or Delete for good of a replacement went.</summary>
public enum ReplacementActionStatus
{
    /// <summary>It was done, with its audit rows; the answer is the download job with its replacement line.</summary>
    Done = 1,

    /// <summary>The caller has no such replacement: there is none, or it is another user's.</summary>
    NotFound = 2,

    /// <summary>The request itself is wrong — Delete for good without the names confirmed —; nothing changed.</summary>
    Invalid = 3,

    /// <summary>The replacement can't take it now — another state, switched off, someone else working on it, a name that
    /// changed — and nothing changed; or it stopped part-way and waits for review. The sentence says which.</summary>
    Refused = 4,
}
