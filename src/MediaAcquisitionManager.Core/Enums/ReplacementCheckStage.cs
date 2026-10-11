namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-27 — where MAM-25's check of a replacement's new files stands while it runs (<see cref="ReplacementJobState.Inspecting"/>,
/// ADR 0035): reading the files in the client and on the disk, or waiting for the library to identify them. Each stage
/// waits an hour of its own, reckoned from its own time on the job. Never stored: derived from the row, and sent by name.
/// </summary>
public enum ReplacementCheckStage
{
    /// <summary>The client's word and the files on the disk are being read; the library wasn't asked to look yet.</summary>
    Files = 1,

    /// <summary>The library was asked to look at the new files, and the check waits for it to list, read and identify
    /// them.</summary>
    Library = 2,
}
