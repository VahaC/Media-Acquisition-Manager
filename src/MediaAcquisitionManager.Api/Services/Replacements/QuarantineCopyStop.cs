namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-40 — why a copy about to move into the quarantine stays where it is, as <see cref="QuarantineCopyCheck"/>
/// found it; each caller words its own sentence.</summary>
public enum QuarantineCopyStop
{
    /// <summary>Its path isn't inside any allowed root now, or doesn't parse.</summary>
    OutsideMediaFolders = 1,

    /// <summary>A library folder holding it isn't ticked, or none holds it (ADR 0033).</summary>
    NotTicked = 2,

    /// <summary>It is a clip of a disc folder, which never moves one file at a time.</summary>
    DiscClip = 3,

    /// <summary>Nothing is at its path.</summary>
    Gone = 4,

    /// <summary>Another file is there, or the same one changed: device, inode, size or modification time.</summary>
    Changed = 5,

    /// <summary>The disk couldn't be read there; the look's own sentence says why.</summary>
    NotRead = 6,
}
