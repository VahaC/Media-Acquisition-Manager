namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — how one download client's whole listing was read for the Download dialog's held marks (ADR 0031). Anything
/// but <see cref="Read"/> is uncertain, never an all-clear. Written by name.
/// </summary>
public enum ReplacementClientCheck
{
    /// <summary>Every torrent of the client was read and every one of its files placed in the app's folders.</summary>
    Read = 1,

    /// <summary>The client answered, but some torrents' files couldn't be placed: no save path, no metadata yet, a path
    /// no mapping covers or two cover differently, an unknown size, or a file the disk didn't show.</summary>
    Partial = 2,

    /// <summary>The client wasn't read: switched off, a type this version can't use, a secret it can't open, or its own
    /// failure.</summary>
    NotRead = 3,
}
