using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-14 — which download jobs are followed in their client, which may be cancelled, and what the client's
/// job says about one (ADR 0014). Pure rules: the poller reads the client and writes the rows.
/// </summary>
/// <remarks>
/// <para>
/// Followed: <see cref="DownloadJobStatus.Submitted"/>, <see cref="DownloadJobStatus.Downloading"/> and
/// <see cref="DownloadJobStatus.Paused"/>, and a <see cref="DownloadJobStatus.Created"/> one — though only
/// once its sending was interrupted, which takes its age to tell and is the poller's to judge. Ended:
/// <see cref="DownloadJobStatus.Downloaded"/>, <see cref="DownloadJobStatus.Failed"/>,
/// <see cref="DownloadJobStatus.NeedsLinking"/> (no linking screen yet) and
/// <see cref="DownloadJobStatus.Cancelled"/>.
/// </para>
/// <para>
/// No sentence lives here: every reason a job shows names its client, so the poller words it.
/// </para>
/// </remarks>
public static class DownloadJobFollowing
{
    /// <summary>The statuses of a job the poller may read from its client, in the order a job goes through
    /// them.</summary>
    public static IReadOnlyList<DownloadJobStatus> FollowedStatuses { get; } =
        [DownloadJobStatus.Created, DownloadJobStatus.Submitted, DownloadJobStatus.Downloading, DownloadJobStatus.Paused];

    /// <summary>Whether a job of <paramref name="status"/> is followed in its client.</summary>
    public static bool IsFollowed(DownloadJobStatus status) => FollowedStatuses.Contains(status);

    /// <summary>
    /// Whether a job of <paramref name="status"/> may be cancelled: a followed one, and one that needs linking
    /// — with no linking screen yet, cancelling is the only way to dismiss it so its release can be sent
    /// again. An ended one has nothing left to stop following.
    /// </summary>
    /// <remarks>
    /// A <see cref="DownloadJobStatus.Created"/> one only once its sending was interrupted, as for following
    /// it: its age tells, and that is the Api's to judge, as the poller judges it — until then the send is
    /// still its sender's, and a cancel would race it.
    /// </remarks>
    public static bool CanCancel(DownloadJobStatus status) => IsFollowed(status) || status == DownloadJobStatus.NeedsLinking;

    /// <summary>
    /// What <paramref name="job"/>, the client's job a followed download was matched to, says about a job now
    /// <paramref name="current"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Downloading, queued and paused jobs take the client's word and its progress; a queued one stays
    /// <see cref="DownloadJobStatus.Submitted"/> until it has started, and once it has, a queued or re-checked
    /// one is still under way (<see cref="DownloadJobStatus.Downloading"/>).
    /// </para>
    /// <para>
    /// <see cref="DownloadJobStatus.Downloaded"/> takes a finished state <b>and</b> a progress of 1, never either
    /// alone: a client re-checking a finished job reports the check's progress under its finished state, and
    /// one still fetching a magnet's metadata reports nothing left to download. A finished state short of 1,
    /// an error and a state this build doesn't know move nothing, except that a job is taken
    /// (<see cref="DownloadJobStatus.Created"/> becomes <see cref="DownloadJobStatus.Submitted"/>) once the
    /// client lists it at all; an error is reported for the job to show, and goes when the client stops
    /// reporting it (MAM-14's decision 2).
    /// </para>
    /// <para>
    /// A <c>null</c> progress from the client always keeps the stored one, as do a re-check's and an unknown
    /// state's. Throws for a job that isn't followed: an ended job is never read from its client again.
    /// </para>
    /// </remarks>
    public static DownloadJobObservation Observe(DownloadJobStatus current, ClientJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!IsFollowed(current))
            throw new ArgumentException($"A {current} job is not followed.", nameof(current));

        var taken = current == DownloadJobStatus.Created ? DownloadJobStatus.Submitted : current;
        return job.State switch
        {
            ClientJobState.Downloading => new(DownloadJobStatus.Downloading, job.Progress, false),
            ClientJobState.Queued => new(
                current is DownloadJobStatus.Created or DownloadJobStatus.Submitted ? DownloadJobStatus.Submitted : DownloadJobStatus.Downloading,
                job.Progress,
                false),
            ClientJobState.Paused => new(DownloadJobStatus.Paused, job.Progress, false),
            ClientJobState.Seeding or ClientJobState.Completed when job.Progress >= 1 => new(DownloadJobStatus.Downloaded, 1, false),
            ClientJobState.Errored => new(taken, job.Progress, true),
            // Finished short of 1 (a re-check), or a state this build doesn't know (moving, one a later
            // client adds): never guessed into a status.
            _ => new(taken, null, false),
        };
    }
}
