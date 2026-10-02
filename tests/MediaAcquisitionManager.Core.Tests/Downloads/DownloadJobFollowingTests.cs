using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-14 — which jobs are followed and cancellable, and what one look at the client's job says about a followed
// one (ADR 0014): every row of the design's table, for every status a followed job can have. Downloaded takes a
// finished state and a progress of 1 together; an error is a reason, not an end (decision 2); a client's null
// progress, a re-check's and an unknown state's keep the stored progress.
public sealed class DownloadJobFollowingTests
{
    [Fact]
    public void FollowedStatuses_are_created_submitted_downloading_and_paused_in_that_order()
    {
        //Arrange

        //Act
        var followed = DownloadJobFollowing.FollowedStatuses;

        //Assert
        Assert.Equal(
            [DownloadJobStatus.Created, DownloadJobStatus.Submitted, DownloadJobStatus.Downloading, DownloadJobStatus.Paused],
            followed);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Created, true, true)]
    [InlineData(DownloadJobStatus.Submitted, true, true)]
    [InlineData(DownloadJobStatus.Downloading, true, true)]
    [InlineData(DownloadJobStatus.Paused, true, true)]
    [InlineData(DownloadJobStatus.NeedsLinking, false, true)]
    [InlineData(DownloadJobStatus.Downloaded, false, false)]
    [InlineData(DownloadJobStatus.Failed, false, false)]
    [InlineData(DownloadJobStatus.Cancelled, false, false)]
    public void IsFollowed_and_CanCancel_answer_for_each_status(DownloadJobStatus status, bool followed, bool cancellable)
    {
        //Arrange

        //Act
        var isFollowed = DownloadJobFollowing.IsFollowed(status);
        var canCancel = DownloadJobFollowing.CanCancel(status);

        //Assert
        // NeedsLinking isn't followed but may be cancelled: with no linking screen yet, that is how it is
        // dismissed so its release can be sent again.
        Assert.Equal(followed, isFollowed);
        Assert.Equal(cancellable, canCancel);
    }

    [Fact]
    public void IsFollowed_and_CanCancel_cover_every_status()
    {
        //Arrange
        // Guard the guard: a status a later ticket adds lands in the theory above, or this fails.
        DownloadJobStatus[] known =
        [
            DownloadJobStatus.Created, DownloadJobStatus.Submitted, DownloadJobStatus.Downloading, DownloadJobStatus.Downloaded,
            DownloadJobStatus.Failed, DownloadJobStatus.NeedsLinking, DownloadJobStatus.Paused, DownloadJobStatus.Cancelled,
        ];

        //Act
        var statuses = Enum.GetValues<DownloadJobStatus>();

        //Assert
        Assert.Equal(known, statuses);
    }

    [Theory]
    // Downloading: the client's word and its progress, whatever the job was.
    [InlineData(DownloadJobStatus.Created, ClientJobState.Downloading, 0.25, DownloadJobStatus.Downloading, 0.25)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Downloading, 0.25, DownloadJobStatus.Downloading, 0.25)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Downloading, 0.5, DownloadJobStatus.Downloading, 0.5)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Downloading, 0.5, DownloadJobStatus.Downloading, 0.5)]
    // A progress of 1 alone is not Downloaded: the state says the client is still at it.
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Downloading, 1d, DownloadJobStatus.Downloading, 1d)]
    // Queued: Submitted until it has started; once it has, a queued or re-checked one is still under way.
    [InlineData(DownloadJobStatus.Created, ClientJobState.Queued, 0d, DownloadJobStatus.Submitted, 0d)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Queued, 0d, DownloadJobStatus.Submitted, 0d)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Queued, 0.4, DownloadJobStatus.Downloading, 0.4)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Queued, 0.4, DownloadJobStatus.Downloading, 0.4)]
    // Paused: the client's word and its progress.
    [InlineData(DownloadJobStatus.Created, ClientJobState.Paused, 0d, DownloadJobStatus.Paused, 0d)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Paused, 0.1, DownloadJobStatus.Paused, 0.1)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Paused, 0.6, DownloadJobStatus.Paused, 0.6)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Paused, 0.6, DownloadJobStatus.Paused, 0.6)]
    // Seeding or Completed at 1: Downloaded, stored as 1.
    [InlineData(DownloadJobStatus.Created, ClientJobState.Seeding, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Seeding, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Seeding, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Seeding, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Created, ClientJobState.Completed, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Completed, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Completed, 1d, DownloadJobStatus.Downloaded, 1d)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Completed, 1d, DownloadJobStatus.Downloaded, 1d)]
    // Seeding or Completed short of 1 (checkingUP reports the check's progress) or without one: nothing moves
    // but a Created job being taken, and the stored progress stays.
    [InlineData(DownloadJobStatus.Created, ClientJobState.Seeding, 0.3, DownloadJobStatus.Submitted, null)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Seeding, 0.3, DownloadJobStatus.Submitted, null)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Seeding, 0.3, DownloadJobStatus.Downloading, null)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Seeding, 0.3, DownloadJobStatus.Paused, null)]
    [InlineData(DownloadJobStatus.Created, ClientJobState.Completed, null, DownloadJobStatus.Submitted, null)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Completed, 0.99, DownloadJobStatus.Submitted, null)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Completed, null, DownloadJobStatus.Downloading, null)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Seeding, null, DownloadJobStatus.Paused, null)]
    // Unknown (moving, a state a later client adds): nothing moves but a Created job being taken.
    [InlineData(DownloadJobStatus.Created, ClientJobState.Unknown, 0.5, DownloadJobStatus.Submitted, null)]
    [InlineData(DownloadJobStatus.Submitted, ClientJobState.Unknown, 0.5, DownloadJobStatus.Submitted, null)]
    [InlineData(DownloadJobStatus.Downloading, ClientJobState.Unknown, 1d, DownloadJobStatus.Downloading, null)]
    [InlineData(DownloadJobStatus.Paused, ClientJobState.Unknown, 0.5, DownloadJobStatus.Paused, null)]
    public void Observe_maps_the_clients_state_and_progress_to_the_jobs(
        DownloadJobStatus current, ClientJobState state, double? clientProgress, DownloadJobStatus expectedStatus, double? expectedProgress)
    {
        //Arrange
        var job = Job(state, clientProgress);

        //Act
        var observation = DownloadJobFollowing.Observe(current, job);

        //Assert
        Assert.Equal(new DownloadJobObservation(expectedStatus, expectedProgress, false), observation);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Created, 0.2, DownloadJobStatus.Submitted, 0.2)]
    [InlineData(DownloadJobStatus.Submitted, 0d, DownloadJobStatus.Submitted, 0d)]
    [InlineData(DownloadJobStatus.Downloading, 0.7, DownloadJobStatus.Downloading, 0.7)]
    [InlineData(DownloadJobStatus.Paused, 0.7, DownloadJobStatus.Paused, 0.7)]
    [InlineData(DownloadJobStatus.Downloading, 1d, DownloadJobStatus.Downloading, 1d)]
    [InlineData(DownloadJobStatus.Downloading, null, DownloadJobStatus.Downloading, null)]
    public void Observe_of_a_client_error_keeps_the_status_and_reports_the_error(
        DownloadJobStatus current, double? clientProgress, DownloadJobStatus expectedStatus, double? expectedProgress)
    {
        //Arrange
        // MAM-14's decision 2: a reason, not an end — even at 1 (missing files of a finished torrent).
        var job = Job(ClientJobState.Errored, clientProgress);

        //Act
        var observation = DownloadJobFollowing.Observe(current, job);

        //Assert
        Assert.Equal(new DownloadJobObservation(expectedStatus, expectedProgress, true), observation);
    }

    [Theory]
    [InlineData(ClientJobState.Downloading)]
    [InlineData(ClientJobState.Queued)]
    [InlineData(ClientJobState.Paused)]
    [InlineData(ClientJobState.Errored)]
    public void Observe_of_a_client_without_a_progress_keeps_the_stored_one(ClientJobState state)
    {
        //Arrange
        var job = Job(state, null);

        //Act
        var observation = DownloadJobFollowing.Observe(DownloadJobStatus.Downloading, job);

        //Assert
        Assert.Null(observation.Progress);
    }

    [Theory]
    [InlineData(DownloadJobStatus.Downloaded)]
    [InlineData(DownloadJobStatus.Failed)]
    [InlineData(DownloadJobStatus.NeedsLinking)]
    [InlineData(DownloadJobStatus.Cancelled)]
    public void Observe_of_a_job_that_is_not_followed_throws(DownloadJobStatus current)
    {
        //Arrange
        var job = Job(ClientJobState.Downloading, 0.5);

        //Act
        var exception = Record.Exception(() => DownloadJobFollowing.Observe(current, job));

        //Assert
        // An ended job is never read from its client again; a caller that does has a bug.
        Assert.IsType<ArgumentException>(exception);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ClientJob Job(ClientJobState state, double? progress) =>
        new("1ec461458feffe9f27ae17e30d0a804e2ab0a88f", "Taken (2008)", state, progress, "Movies", "/downloads/movies/Taken (2008)",
            ["1ec461458feffe9f27ae17e30d0a804e2ab0a88f"]);
}
