namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// Where one download job stands. Stored by name, so a state added by a later ticket needs no
/// migration.
/// </summary>
public enum DownloadJobStatus
{
    /// <summary>
    /// Recorded before the client is called, so a retry after a crash or an ambiguous timeout finds
    /// the job instead of sending the release a second time (specification §3.2, step 5).
    /// </summary>
    Created = 1,

    /// <summary>The client accepted the release.</summary>
    Submitted = 2,

    /// <summary>The client is downloading it.</summary>
    Downloading = 3,

    /// <summary>
    /// The client reports the download complete. MAM-14 — following ends here: the client's finished state
    /// and a progress of 1 together, never either alone, and the job isn't read from the client again.
    /// </summary>
    Downloaded = 4,

    /// <summary>The job failed; its error says why.</summary>
    Failed = 5,

    /// <summary>
    /// The client gave no reliable id for the job and correlating it by its content was ambiguous, so
    /// the owner has to link it (specification §3.2, step 6).
    /// </summary>
    NeedsLinking = 6,

    /// <summary>
    /// MAM-14 — the client holds the download stopped before it completed. Still followed: it goes on as
    /// soon as someone starts it in the client again.
    /// </summary>
    Paused = 7,

    /// <summary>
    /// MAM-14 — the person who sent it stopped following it. Nothing was sent to the client: the torrent
    /// and its files stay there as they were (ADR 0014), and the release may be sent again.
    /// </summary>
    Cancelled = 8,
}
