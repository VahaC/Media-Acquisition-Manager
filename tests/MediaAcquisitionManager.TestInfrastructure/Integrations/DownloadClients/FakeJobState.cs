namespace MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

/// <summary>
/// MAM-30 — a state a job can be in, in no client's words: each <see cref="IFakeDownloadClient"/> plants it as its own
/// client shows it, so the contract suite proves both adapters read it as the same core state.
/// </summary>
public enum FakeJobState
{
    /// <summary>Waiting to download (qBittorrent <c>queuedDL</c>, Transmission status 3).</summary>
    Queued = 1,

    /// <summary>Checking its data before it downloads (qBittorrent <c>checkingDL</c>, Transmission status 2).</summary>
    Checking = 2,

    /// <summary>Downloading (qBittorrent <c>downloading</c>, Transmission status 4).</summary>
    Downloading = 3,

    /// <summary>A magnet still fetching its metadata (qBittorrent <c>metaDL</c>, Transmission status 4 without
    /// metadata).</summary>
    FetchingMetadata = 4,

    /// <summary>Stopped before it completed (qBittorrent <c>stoppedDL</c>, Transmission status 0 with bytes left).</summary>
    Paused = 5,

    /// <summary>Complete and seeding (qBittorrent <c>stalledUP</c>, Transmission status 6).</summary>
    Seeding = 6,

    /// <summary>Complete and stopped (qBittorrent <c>stoppedUP</c>, Transmission status 0 with nothing left).</summary>
    CompletedStopped = 7,

    /// <summary>A local error such as a full disk (qBittorrent <c>error</c>, Transmission error 3).</summary>
    Errored = 8,

    /// <summary>A magnet stopped before its metadata arrived (qBittorrent <c>stoppedDL</c> without metadata, Transmission
    /// status 0 without metadata, which lists nothing left at 0 %): never complete.</summary>
    StoppedWithoutMetadata = 9,
}
