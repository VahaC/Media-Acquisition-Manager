namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>How a request to send a release went (MAM-13). Every status but the first two leaves nothing
/// written and nothing sent.</summary>
public enum DownloadSendStatus
{
    /// <summary>A new job was made and its sending finished, whatever the outcome: the job says it.</summary>
    Created = 1,

    /// <summary>The caller already sent this request: the job it made, and nothing sent again.</summary>
    Replayed = 2,

    /// <summary>A rule refused the request: an id missing, a destination of another client, or a client
    /// that can't take the release's payload.</summary>
    Invalid = 3,

    /// <summary>The release isn't one of the caller's searches' from the last day.</summary>
    ReleaseMissing = 4,

    /// <summary>No download client has the id.</summary>
    ClientMissing = 5,

    /// <summary>No destination has the id.</summary>
    DestinationMissing = 6,

    /// <summary>The caller's request id already made a job for another release, client or destination.</summary>
    RequestIdReused = 7,

    /// <summary>The client can't be sent to: switched off, or a type this build can't send to.</summary>
    ClientUnusable = 8,

    /// <summary>The release can't be fetched: its source was removed, is switched off, or is a type this
    /// build can't fetch from.</summary>
    SourceUnusable = 9,

    /// <summary>The release was already sent to the client by a job that didn't fail (MAM-13's decision 3).</summary>
    AlreadySent = 10,

    /// <summary>MAM-24 — a schedule's ticks don't fit the release's works: no copy, one twice or too many, a copy that
    /// isn't one of theirs, or a confirmation missing, not needed or with no copy ticked.</summary>
    ReplacementInvalid = 11,

    /// <summary>MAM-24 — a ticked copy can't be replaced now: replacement is off, the release is a magnet link or matches
    /// no work any more, or the copy's work or the copy itself stands in the way.</summary>
    ReplacementUnavailable = 12,
}
