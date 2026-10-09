namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-24 — why nothing of a release can be replaced through a destination, the first that applies in this order
/// (ADR 0032, ADR 0033). Written by name.
/// </summary>
public enum ReplacementUnavailableReason
{
    /// <summary>The owner hasn't switched replacement on on the Media folders page (ADR 0033).</summary>
    SwitchedOff = 1,

    /// <summary>The release is a magnet link, whose files the app can't check before sending (ADR 0032).</summary>
    Magnet = 2,

    /// <summary>
    /// The destination can't take a replacement: its client couldn't say where it saves, its folder isn't confirmed
    /// inside a library the app knows, or the app can't look into it for files already there (ADR 0034). The answer's
    /// sentence says which.
    /// </summary>
    DestinationNotUsable = 3,

    /// <summary>The release matches no library work of its kind.</summary>
    NoMatchedWork = 4,
}
