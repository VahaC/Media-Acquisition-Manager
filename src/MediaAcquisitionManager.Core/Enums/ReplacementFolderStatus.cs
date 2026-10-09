namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — what a library folder on the Media folders page's replacement list is to the per-folder ticks (ADR 0033).
/// Written by name.
/// </summary>
public enum ReplacementFolderStatus
{
    /// <summary>A library folder the mappings bring into the app, inside an allowed root: a tick here counts.</summary>
    Available = 1,

    /// <summary>
    /// A library folder the mappings bring into the app, outside every allowed root: the app may not read there, so a
    /// tick here has no effect until the folder is inside one.
    /// </summary>
    OutsideMediaFolders = 2,

    /// <summary>
    /// A stored tick whose path is no longer any library folder the mappings bring into the app (the library dropped
    /// it, or a mapping changed): listed, ticked, only so the owner can untick it; it has no effect.
    /// </summary>
    NoLongerALibraryFolder = 3,
}
