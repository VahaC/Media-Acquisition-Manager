namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-27 — how far MAM-25's check got with one new file of a replacement's download (ADR 0035), as the job's page shows
/// it: the client's word, the disk, then the library listing, reading and identifying it. Derived on the server from the
/// file's evidence row (<c>ReplacementJobProgress.NewFileProgressOf</c>), never stored, and sent by name.
/// </summary>
public enum ReplacementNewFileProgress
{
    /// <summary>It stopped the replacement; its problem says why. Never while the check still runs: a problem a pass found
    /// while it waits on another file stops nothing yet, and the file says how far the check got with it.</summary>
    Stopped = 1,

    /// <summary>The client hasn't finished downloading it.</summary>
    Downloading = 2,

    /// <summary>Downloaded, but not read on the disk yet.</summary>
    NotChecked = 3,

    /// <summary>Read on the disk; the library wasn't asked to look at it yet.</summary>
    Checked = 4,

    /// <summary>The library was asked to look at it, and the check hasn't read the library since.</summary>
    AskedToLook = 5,

    /// <summary>The library doesn't list it yet.</summary>
    NotListed = 6,

    /// <summary>The library lists it but hasn't finished reading it.</summary>
    NotRead = 7,

    /// <summary>The library made it neither a film nor an episode — an extra —: it is simply added, and replaces
    /// nothing.</summary>
    Extra = 8,

    /// <summary>The library gave it no id that tells which work it is yet.</summary>
    NoId = 9,

    /// <summary>The library made it an episode but gave it no season and episode number yet.</summary>
    NotNumbered = 10,

    /// <summary>The library identified it as one of the replacement's works.</summary>
    Identified = 11,
}
