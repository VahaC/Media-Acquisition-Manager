using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-24/MAM-25 — which replacement jobs are still going (ADR 0030–0035). MAM-25's states are checked by the verifier and
// never followed — a followed Inspecting job would be mirrored back to Downloaded on every pass of the download jobs —,
// and a job being checked or verified still holds its old copies. MAM-26 — the mover takes up a verified job and one it is
// moving, neither followed nor checked; a move, a restore and a deletion under way are what a restart may cut; and a job
// holds its copies until its names are all deleted or all back.
public sealed class ReplacementJobStatesTests
{
    [Fact]
    public void Followed_are_the_states_before_the_download_is_done_and_the_owners_review()
    {
        //Arrange

        //Act
        var followed = ReplacementJobStates.Followed;

        //Assert
        Assert.Equal(
            [
                ReplacementJobState.Scheduled, ReplacementJobState.Submitted, ReplacementJobState.Downloading, ReplacementJobState.Paused,
                ReplacementJobState.NeedsReview,
            ],
            followed);
    }

    [Fact]
    public void Verifying_are_a_downloaded_job_and_one_being_checked()
    {
        //Arrange

        //Act
        var verifying = ReplacementJobStates.Verifying;

        //Assert
        Assert.Equal([ReplacementJobState.Downloaded, ReplacementJobState.Inspecting], verifying);
        Assert.Empty(verifying.Intersect(ReplacementJobStates.Followed));
    }

    [Fact]
    public void Quarantine_are_a_verified_job_and_one_being_moved()
    {
        //Arrange

        //Act
        var quarantine = ReplacementJobStates.Quarantine;

        //Assert
        Assert.Equal([ReplacementJobState.Verified, ReplacementJobState.Quarantining], quarantine);
        // Neither following nor the check ever takes the mover's jobs back.
        Assert.Empty(quarantine.Intersect(ReplacementJobStates.Followed));
        Assert.Empty(quarantine.Intersect(ReplacementJobStates.Verifying));
    }

    [Fact]
    public void Unsettled_are_a_move_a_restore_and_a_deletion_under_way()
    {
        //Arrange

        //Act
        var unsettled = ReplacementJobStates.Unsettled;

        //Assert
        Assert.Equal([ReplacementJobState.Quarantining, ReplacementJobState.Restoring, ReplacementJobState.Deleting], unsettled);
        // Each still holds its copies, and none is followed or checked.
        Assert.Empty(unsettled.Except(ReplacementJobStates.Open));
        Assert.Empty(unsettled.Intersect(ReplacementJobStates.Followed));
        Assert.Empty(unsettled.Intersect(ReplacementJobStates.Verifying));
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled, true)]
    [InlineData(ReplacementJobState.Submitted, true)]
    [InlineData(ReplacementJobState.Downloading, true)]
    [InlineData(ReplacementJobState.Paused, true)]
    [InlineData(ReplacementJobState.Downloaded, true)]
    [InlineData(ReplacementJobState.Inspecting, true)]
    [InlineData(ReplacementJobState.Verified, true)]
    [InlineData(ReplacementJobState.Quarantining, true)]
    [InlineData(ReplacementJobState.Quarantined, true)]
    [InlineData(ReplacementJobState.Restoring, true)]
    [InlineData(ReplacementJobState.Deleting, true)]
    [InlineData(ReplacementJobState.NeedsReview, true)]
    [InlineData(ReplacementJobState.Failed, false)]
    [InlineData(ReplacementJobState.Cancelled, false)]
    [InlineData(ReplacementJobState.Completed, false)]
    [InlineData(ReplacementJobState.Restored, false)]
    public void Open_holds_every_state_but_the_ends(ReplacementJobState state, bool open)
    {
        //Arrange

        //Act
        var holds = ReplacementJobStates.Open.Contains(state);

        //Assert
        Assert.Equal(open, holds);
    }

    [Fact]
    public void Open_answers_for_every_state()
    {
        //Arrange
        // Guard the guard: a state a later ticket adds lands in the theory above, or this fails.
        ReplacementJobState[] known =
        [
            ReplacementJobState.Scheduled, ReplacementJobState.Submitted, ReplacementJobState.Downloading, ReplacementJobState.Paused,
            ReplacementJobState.Downloaded, ReplacementJobState.NeedsReview, ReplacementJobState.Failed, ReplacementJobState.Cancelled,
            ReplacementJobState.Inspecting, ReplacementJobState.Verified, ReplacementJobState.Quarantining, ReplacementJobState.Quarantined,
            ReplacementJobState.Completed, ReplacementJobState.Restored, ReplacementJobState.Restoring, ReplacementJobState.Deleting,
        ];

        //Act
        var all = Enum.GetValues<ReplacementJobState>();

        //Assert
        Assert.Equal(known.Order(), all.Order());
    }
}
