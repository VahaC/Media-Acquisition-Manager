namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>MAM-26 — what keeps a rename into the quarantine from working in a folder (ADR 0033's hint). Stored and
/// sent by name.</summary>
public enum RenamePlaceProblem
{
    /// <summary>The folder isn't on the same mount as its allowed root, where the quarantine is.</summary>
    OtherMount = 1,

    /// <summary>The folder's mount is read-only.</summary>
    ReadOnly = 2,

    /// <summary>The app's user may not write to and search the folder, or the root's <c>.mam-quarantine</c> — the root
    /// itself while there is none yet.</summary>
    NotWritable = 3,

    /// <summary>The app couldn't tell: not Linux, the folder is gone, or the mount table can't be read.</summary>
    CouldNotCheck = 4,
}
