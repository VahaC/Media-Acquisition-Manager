using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-27 — where a replacement and each of its rows stand, as the Jobs line and the job's page show them (ADR 0030, ADR
// 0035), derived from the stored rows and never stored. A review is the mover's when the mover ever took the job up, the
// check's when the check started, and the download's otherwise; a running check is in the library's stage once the
// library was asked, and waits an hour from that or from its start. A copy dropped by the owner is out of the job before
// anything else; a name that left its folder says more than the check did; an ended job — cancelled, failed, restored or
// deleted for good — leaves the rest where they are; then the copy's problem once it stopped the job for review, its pair,
// and whether the check is over. A new file goes through the client, the disk and the library in that order, its own
// problem first once the check is over, and an episode is identified only once it is numbered. A problem a pass found while
// the check still waits on another file stops nothing yet, for a copy or a new file.
public sealed class ReplacementJobProgressTests
{
    private static readonly DateTime StartedUtc = new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
    private static readonly DateTime AskedUtc = StartedUtc.AddMinutes(3).AddTicks(7);
    private static readonly DateTime ReadUtc = AskedUtc.AddSeconds(15);
    private static readonly Guid WorkId = new("a1b2c3d4-0000-4000-8000-000000000027");
    private const string StopReason = "Jellyfin “Living room” identified the new file “Alien.1979.mkv” as another work: Aliens (1986).";

    /// <summary>A film the check got all the way through with: downloaded, read on the disk, listed, read and identified
    /// by the library.</summary>
    private static readonly NewFileEvidence IdentifiedFilm = new(
        null, 1, StartedUtc.AddSeconds(5), AskedUtc, ReadUtc, "library-item-1", LibraryFileKind.Movie, true, WorkId, null, null);

    /// <summary>An episode the check got all the way through with, at season 2, episode 5.</summary>
    private static readonly NewFileEvidence IdentifiedEpisode = IdentifiedFilm with { Kind = LibraryFileKind.Episode, SeasonNumber = 2, EpisodeNumber = 5 };

    /// <summary>Every state but a review.</summary>
    public static TheoryData<ReplacementJobState> NotInReview => [.. Enum.GetValues<ReplacementJobState>().Where(state => state != ReplacementJobState.NeedsReview)];

    /// <summary>Every state but a running check.</summary>
    public static TheoryData<ReplacementJobState> NotBeingChecked => [.. Enum.GetValues<ReplacementJobState>().Where(state => state != ReplacementJobState.Inspecting)];

    // ── the stop of a review ────────────────────────────────────────────────────────────────────

    [Fact]
    public void StopOf_a_review_the_mover_marked_is_the_moves_even_after_a_check()
    {
        //Arrange

        //Act
        var stops = (
            ReplacementJobProgress.StopOf(ReplacementJobState.NeedsReview, StartedUtc, AskedUtc),
            ReplacementJobProgress.StopOf(ReplacementJobState.NeedsReview, null, AskedUtc));

        //Assert
        Assert.Equal(((ReplacementStop?)ReplacementStop.Move, (ReplacementStop?)ReplacementStop.Move), stops);
    }

    [Fact]
    public void StopOf_a_review_after_the_check_started_and_before_the_mover_is_the_checks()
    {
        //Arrange

        //Act
        var stop = ReplacementJobProgress.StopOf(ReplacementJobState.NeedsReview, StartedUtc, null);

        //Assert
        Assert.Equal(ReplacementStop.Check, stop);
    }

    [Fact]
    public void StopOf_a_review_with_no_check_started_is_the_downloads()
    {
        //Arrange

        //Act
        var stop = ReplacementJobProgress.StopOf(ReplacementJobState.NeedsReview, null, null);

        //Assert
        Assert.Equal(ReplacementStop.Download, stop);
    }

    [Theory]
    [MemberData(nameof(NotInReview))]
    public void StopOf_a_job_not_in_review_is_none(ReplacementJobState state)
    {
        //Arrange

        //Act
        var stop = ReplacementJobProgress.StopOf(state, StartedUtc, AskedUtc);

        //Assert
        Assert.Null(stop);
    }

    // ── the stage of a running check ────────────────────────────────────────────────────────────

    [Fact]
    public void StageOf_a_check_whose_library_was_asked_is_the_librarys()
    {
        //Arrange

        //Act
        var stage = ReplacementJobProgress.StageOf(ReplacementJobState.Inspecting, AskedUtc);

        //Assert
        Assert.Equal(ReplacementCheckStage.Library, stage);
    }

    [Fact]
    public void StageOf_a_check_whose_library_wasnt_asked_is_the_files()
    {
        //Arrange

        //Act
        var stage = ReplacementJobProgress.StageOf(ReplacementJobState.Inspecting, null);

        //Assert
        Assert.Equal(ReplacementCheckStage.Files, stage);
    }

    [Theory]
    [MemberData(nameof(NotBeingChecked))]
    public void StageOf_a_job_not_being_checked_is_none(ReplacementJobState state)
    {
        //Arrange

        //Act
        var stage = ReplacementJobProgress.StageOf(state, AskedUtc);

        //Assert
        Assert.Null(stage);
    }

    // ── until when a running check waits ────────────────────────────────────────────────────────

    [Fact]
    public void CheckWaitsUntilUtc_in_the_librarys_stage_is_an_hour_from_the_ask()
    {
        //Arrange

        //Act
        var until = ReplacementJobProgress.CheckWaitsUntilUtc(ReplacementJobState.Inspecting, StartedUtc, AskedUtc);

        //Assert
        Assert.Equal(AskedUtc.AddHours(1), until);
    }

    [Fact]
    public void CheckWaitsUntilUtc_in_the_files_stage_is_an_hour_from_the_start()
    {
        //Arrange

        //Act
        var until = ReplacementJobProgress.CheckWaitsUntilUtc(ReplacementJobState.Inspecting, StartedUtc, null);

        //Assert
        Assert.Equal(StartedUtc.AddHours(1), until);
    }

    [Fact]
    public void CheckWaitsUntilUtc_of_a_check_with_no_time_stored_is_none()
    {
        //Arrange

        //Act
        var until = ReplacementJobProgress.CheckWaitsUntilUtc(ReplacementJobState.Inspecting, null, null);

        //Assert
        Assert.Null(until);
    }

    [Theory]
    [MemberData(nameof(NotBeingChecked))]
    public void CheckWaitsUntilUtc_of_a_job_not_being_checked_is_none(ReplacementJobState state)
    {
        //Arrange

        //Act
        var until = ReplacementJobProgress.CheckWaitsUntilUtc(state, StartedUtc, AskedUtc);

        //Assert
        Assert.Null(until);
    }

    // ── a ticked copy ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReplacementJobState.NeedsReview, ReplacementNameState.Quarantined)]
    [InlineData(ReplacementJobState.Cancelled, null)]
    [InlineData(ReplacementJobState.Verified, null)]
    public void CopyStatusOf_a_dropped_copy_is_dropped_whatever_else_it_has(ReplacementJobState jobState, ReplacementNameState? videoNameState)
    {
        //Arrange

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(jobState, dropped: true, WorkId, StopReason, videoNameState);

        //Assert
        Assert.Equal(ReplacementCopyStatus.Dropped, status);
    }

    [Theory]
    [InlineData(ReplacementNameState.Quarantined, ReplacementCopyStatus.InQuarantine)]
    [InlineData(ReplacementNameState.Deleted, ReplacementCopyStatus.Deleted)]
    [InlineData(ReplacementNameState.Restored, ReplacementCopyStatus.Restored)]
    [InlineData(ReplacementNameState.Lost, ReplacementCopyStatus.Lost)]
    [InlineData(ReplacementNameState.Moving, ReplacementCopyStatus.Moving)]
    [InlineData(ReplacementNameState.Restoring, ReplacementCopyStatus.Moving)]
    [InlineData(ReplacementNameState.Deleting, ReplacementCopyStatus.Moving)]
    public void CopyStatusOf_a_copy_whose_video_left_its_folder_says_where_the_video_is(ReplacementNameState videoNameState, ReplacementCopyStatus expected)
    {
        //Arrange
        // A problem, a pair and an ended job: what the name says still comes first.

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(ReplacementJobState.Failed, dropped: false, WorkId, StopReason, videoNameState);

        //Assert
        Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(ReplacementJobState.Cancelled, null)]
    [InlineData(ReplacementJobState.Failed, null)]
    [InlineData(ReplacementJobState.Cancelled, ReplacementNameState.Planned)]
    [InlineData(ReplacementJobState.Failed, ReplacementNameState.Stayed)]
    // A move that stopped part-way, then the owner's Restore: the copies it never reached stay with their names as planned.
    [InlineData(ReplacementJobState.Restored, ReplacementNameState.Planned)]
    [InlineData(ReplacementJobState.Restored, ReplacementNameState.Stayed)]
    [InlineData(ReplacementJobState.Restored, null)]
    [InlineData(ReplacementJobState.Completed, null)]
    public void CopyStatusOf_a_copy_of_an_ended_job_still_at_its_old_path_stays(ReplacementJobState jobState, ReplacementNameState? videoNameState)
    {
        //Arrange

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(jobState, dropped: false, WorkId, StopReason, videoNameState);

        //Assert
        Assert.Equal(ReplacementCopyStatus.Stays, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ReplacementNameState.Planned)]
    [InlineData(ReplacementNameState.Stayed)]
    public void CopyStatusOf_a_copy_with_a_problem_stopped_the_job_even_beside_its_pair(ReplacementNameState? videoNameState)
    {
        //Arrange

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(ReplacementJobState.NeedsReview, dropped: false, WorkId, StopReason, videoNameState);

        //Assert
        Assert.Equal(ReplacementCopyStatus.Stopped, status);
    }

    [Fact]
    public void CopyStatusOf_a_copy_with_a_problem_a_pass_found_while_the_check_still_waits_on_another_file_waits()
    {
        //Arrange
        // As a waiting pass keeps it: the problem it found, and no pair until the job is decided.

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(ReplacementJobState.Inspecting, dropped: false, null, StopReason, null);

        //Assert
        Assert.Equal(ReplacementCopyStatus.Waiting, status);
    }

    [Theory]
    [InlineData(ReplacementJobState.Inspecting)]
    [InlineData(ReplacementJobState.Verified)]
    [InlineData(ReplacementJobState.NeedsReview)]
    public void CopyStatusOf_a_paired_copy_with_no_problem_is_paired(ReplacementJobState jobState)
    {
        //Arrange

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(jobState, dropped: false, WorkId, null, null);

        //Assert
        Assert.Equal(ReplacementCopyStatus.Paired, status);
    }

    [Theory]
    [InlineData(ReplacementJobState.Scheduled, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Submitted, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Downloading, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Paused, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Downloaded, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Inspecting, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Restoring, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Deleting, ReplacementCopyStatus.Waiting)]
    [InlineData(ReplacementJobState.Verified, ReplacementCopyStatus.NotPaired)]
    [InlineData(ReplacementJobState.NeedsReview, ReplacementCopyStatus.NotPaired)]
    [InlineData(ReplacementJobState.Quarantining, ReplacementCopyStatus.NotPaired)]
    [InlineData(ReplacementJobState.Quarantined, ReplacementCopyStatus.NotPaired)]
    [InlineData(ReplacementJobState.Completed, ReplacementCopyStatus.Stays)]
    [InlineData(ReplacementJobState.Restored, ReplacementCopyStatus.Stays)]
    [InlineData(ReplacementJobState.Cancelled, ReplacementCopyStatus.Stays)]
    [InlineData(ReplacementJobState.Failed, ReplacementCopyStatus.Stays)]
    public void CopyStatusOf_an_unpaired_copy_with_no_problem_waits_until_the_check_is_over(ReplacementJobState jobState, ReplacementCopyStatus expected)
    {
        //Arrange

        //Act
        var status = ReplacementJobProgress.CopyStatusOf(jobState, dropped: false, null, null, null);

        //Assert
        Assert.Equal(expected, status);
    }

    [Fact]
    public void CopyStatusOf_answers_for_every_job_state()
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

    // ── a new file ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(NotBeingChecked))]
    public void NewFileProgressOf_a_file_with_a_problem_stopped_the_job_whatever_else_it_has(ReplacementJobState jobState)
    {
        //Arrange
        var file = IdentifiedFilm with { Problem = StopReason };

        //Act
        var progress = file.Progress(jobState);

        //Assert
        Assert.Equal(ReplacementNewFileProgress.Stopped, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_file_with_a_problem_a_pass_found_while_the_check_still_waits_on_another_file_says_how_far_the_check_got()
    {
        //Arrange
        // Identified as another work, which stops the job only once every new file is listed and read (Vasyl, 2026-10-10).
        var file = IdentifiedFilm with { Problem = StopReason, MediaWorkId = null };

        //Act
        var progress = file.Progress(ReplacementJobState.Inspecting);

        //Assert
        Assert.Equal(ReplacementNewFileProgress.NoId, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_file_the_client_hasnt_finished_is_downloading()
    {
        //Arrange
        var file = IdentifiedFilm with { ClientProgress = 0.42 };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.Downloading, progress);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(null)]
    public void NewFileProgressOf_a_file_the_disk_wasnt_read_for_is_not_checked(double? clientProgress)
    {
        //Arrange
        var file = IdentifiedFilm with { ClientProgress = clientProgress, CheckedUtc = null };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.NotChecked, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_file_read_on_the_disk_before_the_library_was_asked_is_checked()
    {
        //Arrange
        var file = IdentifiedFilm with { LibraryAskedUtc = null };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.Checked, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_file_the_library_was_asked_about_and_not_read_since_is_asked_to_look()
    {
        //Arrange
        var file = IdentifiedFilm with { ReadUtc = null };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.AskedToLook, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_file_the_library_doesnt_list_is_not_listed()
    {
        //Arrange
        var file = IdentifiedFilm with { LibraryItemId = null, Kind = null, Refreshed = false, MediaWorkId = null };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.NotListed, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_file_the_library_hasnt_finished_reading_is_not_read()
    {
        //Arrange
        var file = IdentifiedFilm with { Refreshed = false };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.NotRead, progress);
    }

    [Theory]
    [InlineData(LibraryFileKind.Other)]
    [InlineData(null)]
    public void NewFileProgressOf_a_file_the_library_made_neither_a_film_nor_an_episode_is_an_extra(LibraryFileKind? kind)
    {
        //Arrange
        var file = IdentifiedFilm with { Kind = kind, MediaWorkId = null };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.Extra, progress);
    }

    [Theory]
    [InlineData(LibraryFileKind.Movie)]
    [InlineData(LibraryFileKind.Episode)]
    public void NewFileProgressOf_a_film_or_episode_identified_as_no_work_has_no_id(LibraryFileKind kind)
    {
        //Arrange
        var file = IdentifiedEpisode with { Kind = kind, MediaWorkId = null };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.NoId, progress);
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(2, null)]
    [InlineData(null, null)]
    public void NewFileProgressOf_an_episode_without_its_season_or_number_is_not_numbered(int? seasonNumber, int? episodeNumber)
    {
        //Arrange
        var file = IdentifiedEpisode with { SeasonNumber = seasonNumber, EpisodeNumber = episodeNumber };

        //Act
        var progress = file.Progress();

        //Assert
        Assert.Equal(ReplacementNewFileProgress.NotNumbered, progress);
    }

    [Fact]
    public void NewFileProgressOf_a_film_or_a_numbered_episode_of_a_work_is_identified()
    {
        //Arrange

        //Act
        var progress = (IdentifiedFilm.Progress(), IdentifiedEpisode.Progress());

        //Assert
        // A film has no place: no season or number is asked of it.
        Assert.Equal((ReplacementNewFileProgress.Identified, ReplacementNewFileProgress.Identified), progress);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One new file's evidence row, as <see cref="ReplacementJobProgress.NewFileProgressOf"/> reads it — of a job whose
    /// check still runs, unless another state is given.</summary>
    private sealed record NewFileEvidence(
        string? Problem,
        double? ClientProgress,
        DateTime? CheckedUtc,
        DateTime? LibraryAskedUtc,
        DateTime? ReadUtc,
        string? LibraryItemId,
        LibraryFileKind? Kind,
        bool Refreshed,
        Guid? MediaWorkId,
        int? SeasonNumber,
        int? EpisodeNumber)
    {
        public ReplacementNewFileProgress Progress(ReplacementJobState jobState = ReplacementJobState.Inspecting) => ReplacementJobProgress.NewFileProgressOf(
            jobState, Problem, ClientProgress, CheckedUtc, LibraryAskedUtc, ReadUtc, LibraryItemId, Kind, Refreshed, MediaWorkId, SeasonNumber, EpisodeNumber);
    }
}
