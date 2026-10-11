using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-27 — which of the owner's buttons a replacement offers, one rule each for the Jobs line, the job's page and the
// actions (ADR 0030, ADR 0033). Retry: a move's stop always, a check's stop unless an undropped copy can only be dropped,
// never a download the app can't follow nor any other state. Keep both: every state before an old copy starts to move, while
// none of its names left its folder. Drop (Vasyl, 2026-10-11): only from a review the check or the mover stopped, a copy
// not dropped yet and still in its folder, and never the last one. What each action then does over HTTP is pinned in its
// own HTTP tests.
public sealed class ReplacementJobOffersTests
{
    [Theory]
    [InlineData(ReplacementStop.Move, 0, true)]
    [InlineData(ReplacementStop.Check, 0, true)]
    [InlineData(ReplacementStop.Check, 1, false)]
    [InlineData(ReplacementStop.Check, 3, false)]
    [InlineData(ReplacementStop.Download, 0, false)]
    [InlineData(null, 0, false)]
    public void CanRetry_offers_a_new_check_only_while_no_copy_can_only_be_dropped(ReplacementStop? stop, int dropOnlyCopyCount, bool offered)
    {
        //Arrange

        //Act
        var canRetry = ReplacementJobOffers.CanRetry(stop, dropOnlyCopyCount);

        //Assert
        Assert.Equal(offered, canRetry);
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled, true)]
    [InlineData(ReplacementJobState.Submitted, true)]
    [InlineData(ReplacementJobState.Downloading, true)]
    [InlineData(ReplacementJobState.Paused, true)]
    [InlineData(ReplacementJobState.Downloaded, true)]
    [InlineData(ReplacementJobState.Inspecting, true)]
    [InlineData(ReplacementJobState.Verified, true)]
    [InlineData(ReplacementJobState.NeedsReview, true)]
    [InlineData(ReplacementJobState.Quarantining, false)]
    [InlineData(ReplacementJobState.Quarantined, false)]
    [InlineData(ReplacementJobState.Restoring, false)]
    [InlineData(ReplacementJobState.Deleting, false)]
    [InlineData(ReplacementJobState.Failed, false)]
    [InlineData(ReplacementJobState.Cancelled, false)]
    [InlineData(ReplacementJobState.Completed, false)]
    [InlineData(ReplacementJobState.Restored, false)]
    public void CanCancel_offers_keep_both_before_an_old_copy_starts_to_move(ReplacementJobState state, bool offered)
    {
        //Arrange

        //Act
        var canCancel = ReplacementJobOffers.CanCancel(state, holdsTakenNames: false);

        //Assert
        Assert.Equal(offered, canCancel);
    }

    [Fact]
    public void CanCancel_offers_no_keep_both_once_a_name_left_its_folder()
    {
        //Arrange
        // A review the mover stopped part-way: Restore puts back what is in the quarantine.

        //Act
        var canCancel = ReplacementJobOffers.CanCancel(ReplacementJobState.NeedsReview, holdsTakenNames: true);

        //Assert
        Assert.False(canCancel);
    }

    [Theory]
    [InlineData(ReplacementStop.Check)]
    [InlineData(ReplacementStop.Move)]
    public void CanDrop_offers_a_copy_of_a_review_the_check_or_the_mover_stopped(ReplacementStop stop)
    {
        //Arrange

        //Act
        var canDrop = ReplacementJobOffers.CanDrop(ReplacementJobState.NeedsReview, stop, dropped: false, holdsTakenNames: false, undroppedCopyCount: 2);

        //Assert
        Assert.True(canDrop);
    }

    [Fact]
    public void CanDrop_offers_no_copy_of_a_review_whose_download_the_app_cant_follow()
    {
        //Arrange

        //Act
        var canDrop = ReplacementJobOffers.CanDrop(
            ReplacementJobState.NeedsReview, ReplacementStop.Download, dropped: false, holdsTakenNames: false, undroppedCopyCount: 2);

        //Assert
        Assert.False(canDrop);
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled)]
    [InlineData(ReplacementJobState.Downloading)]
    [InlineData(ReplacementJobState.Downloaded)]
    [InlineData(ReplacementJobState.Inspecting)]
    [InlineData(ReplacementJobState.Verified)]
    [InlineData(ReplacementJobState.Quarantining)]
    [InlineData(ReplacementJobState.Quarantined)]
    [InlineData(ReplacementJobState.Cancelled)]
    [InlineData(ReplacementJobState.Completed)]
    public void CanDrop_offers_no_copy_of_a_job_not_stopped_for_review(ReplacementJobState state)
    {
        //Arrange
        // A stop as a review would have it: the state alone refuses.

        //Act
        var canDrop = ReplacementJobOffers.CanDrop(state, ReplacementStop.Check, dropped: false, holdsTakenNames: false, undroppedCopyCount: 2);

        //Assert
        Assert.False(canDrop);
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 0)]
    public void CanDrop_offers_no_copy_dropped_already_out_of_its_folder_or_the_last_one(bool dropped, bool holdsTakenNames, int undroppedCopyCount)
    {
        //Arrange

        //Act
        var canDrop = ReplacementJobOffers.CanDrop(ReplacementJobState.NeedsReview, ReplacementStop.Move, dropped, holdsTakenNames, undroppedCopyCount);

        //Assert
        Assert.False(canDrop);
    }
}
