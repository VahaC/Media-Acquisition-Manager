using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — check 1 of a replacement's new files (ADR 0035): the client's own word, read fresh, that the download finished
// without an error and that every new file of it is complete. Gone or errored is the owner's review; still downloading,
// moving or re-checking, or the files not yet in the save folder or not yet listed, is a wait; a wanted video file short
// of 100 %, or one the client says too little of, is the owner's review, and so is a download with no video file at all.
// Skipped files and companions are no new files. Every sentence names the client by its integration's name.
public sealed class NewFileCompletenessTests
{
    private const string Client = "qBittorrent";

    [Fact]
    public void OfJob_of_a_download_the_client_no_longer_lists_is_the_owners_review()
    {
        //Arrange

        //Act
        var result = NewFileCompleteness.OfJob(null, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Review, "“qBittorrent” no longer has this download, so the app can't confirm its files are complete."),
            (result.Status, result.Problem));
        Assert.Empty(result.NewFiles);
    }

    [Fact]
    public void OfJob_of_a_download_the_client_reports_an_error_for_is_the_owners_review()
    {
        //Arrange
        var job = Job(ClientJobState.Errored, 1);

        //Act
        var result = NewFileCompleteness.OfJob(job, Client);

        //Assert
        // Retry in MAM-27 reads the client again, so an error is the owner's to look at, not a failure.
        Assert.Equal(
            (NewFileCompletenessStatus.Review, "“qBittorrent” reports an error for this download, so the app can't confirm its files are complete."),
            (result.Status, result.Problem));
    }

    [Theory]
    [InlineData(ClientJobState.Queued, 1.0)]
    [InlineData(ClientJobState.Downloading, 0.5)]
    [InlineData(ClientJobState.Paused, 1.0)]
    // A finished state short of 1 is a re-check.
    [InlineData(ClientJobState.Seeding, 0.97)]
    [InlineData(ClientJobState.Completed, 0.999)]
    public void OfJob_of_a_download_not_finished_waits(ClientJobState state, double progress)
    {
        //Arrange
        var job = Job(state, progress);

        //Act
        var result = NewFileCompleteness.OfJob(job, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Wait, "“qBittorrent” doesn't report this download as finished, so its files can't be checked."),
            (result.Status, result.Problem));
    }

    [Fact]
    public void OfJob_of_a_finished_download_without_a_progress_waits()
    {
        //Arrange
        var job = Job(ClientJobState.Seeding, null);

        //Act
        var result = NewFileCompleteness.OfJob(job, Client);

        //Assert
        Assert.Equal(NewFileCompletenessStatus.Wait, result.Status);
    }

    [Fact]
    public void OfJob_of_a_download_in_a_state_the_app_does_not_know_waits()
    {
        //Arrange
        // qBittorrent's "moving" maps to Unknown: never guessed into a finished state.
        var job = Job(ClientJobState.Unknown, 1);

        //Act
        var result = NewFileCompleteness.OfJob(job, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Wait,
                "“qBittorrent” reports this download in a state the app doesn't know, such as moving its files, so its files can't be checked."),
            (result.Status, result.Problem));
    }

    [Fact]
    public void OfJob_of_a_finished_download_whose_content_is_still_outside_its_save_folder_waits()
    {
        //Arrange
        var job = Job(ClientJobState.Seeding, 1, savePath: "/downloads/movies", contentPath: "/downloads/incomplete/Alien.1979.mkv");

        //Act
        var result = NewFileCompleteness.OfJob(job, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Wait, "“qBittorrent” still keeps this download's files outside its save folder, so they can't be checked where they will stay."),
            (result.Status, result.Problem));
    }

    [Theory]
    [InlineData(ClientJobState.Seeding, "/downloads/movies", "/downloads/movies/Alien (1979)")]
    [InlineData(ClientJobState.Completed, "/downloads/movies", "/downloads/movies/Alien.1979.mkv")]
    // The NoSubfolder layout of several files: the content is the save folder itself.
    [InlineData(ClientJobState.Seeding, "/downloads/tv", "/downloads/tv")]
    // A path the client doesn't give says nothing here: the verifier can't place the files then, and says so itself.
    [InlineData(ClientJobState.Seeding, null, "/downloads/movies/Alien.1979.mkv")]
    [InlineData(ClientJobState.Seeding, "/downloads/movies", null)]
    public void OfJob_of_a_finished_download_in_its_save_folder_is_complete_so_its_files_may_be_read(
        ClientJobState state, string? savePath, string? contentPath)
    {
        //Arrange
        var job = Job(state, 1, savePath, contentPath);

        //Act
        var result = NewFileCompleteness.OfJob(job, Client);

        //Assert
        Assert.Equal((NewFileCompletenessStatus.Complete, (string?)null), (result.Status, result.Problem));
        Assert.Empty(result.NewFiles);
    }

    [Fact]
    public void OfFiles_gives_the_wanted_video_files_with_their_places_in_the_clients_list()
    {
        //Arrange
        ClientJobFile[] files =
        [
            new("Show S02/Show.S02E01.mkv", 1_000, 1, true),
            new("Show S02/Show.S02E01.srt", 10, 1, true),
            new("Show S02/Sample/sample.mkv", 100, 0, false),
            new("Show S02/Show.S02E02.MKV", 2_000, 1, true),
            new("Show S02/Show.S02E02.nfo", 1, 1, true),
            new("Show S02/Show.S02E03.mka", 500, 1, true),
        ];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        // Subtitles, an .nfo and an audio track are no new copies; a skipped sample is not downloaded at all.
        Assert.Equal(NewFileCompletenessStatus.Complete, result.Status);
        Assert.Equal([(0, "Show S02/Show.S02E01.mkv"), (3, "Show S02/Show.S02E02.MKV")], result.NewFiles.Select(file => (file.Position, file.File.RelativePath)));
        Assert.Null(result.Problem);
    }

    [Fact]
    public void OfFiles_of_a_wanted_video_file_not_complete_is_the_owners_review_with_how_far_it_came()
    {
        //Arrange
        ClientJobFile[] files =
        [
            new("Pack/Alien.1979.mkv", 1_000, 1, true),
            new("Pack/Aliens.1986.mkv", 1_000, 0.739, true),
            new("Pack/Alien3.1992.mkv", 1_000, 0.2, true),
        ];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        // 100 % for the whole download doesn't say which file of a pack is complete; rounded down, never up to 100.
        Assert.Equal(
            (NewFileCompletenessStatus.Review, "“qBittorrent” hasn't finished “Pack/Aliens.1986.mkv” (73 % done)."),
            (result.Status, result.Problem));
        Assert.Empty(result.NewFiles);
    }

    [Fact]
    public void OfFiles_of_a_file_a_byte_short_never_reads_as_done()
    {
        //Arrange
        ClientJobFile[] files = [new("Alien.1979.mkv", 1_000_000, 0.9999999, true)];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        Assert.Equal("“qBittorrent” hasn't finished “Alien.1979.mkv” (99 % done).", result.Problem);
    }

    [Fact]
    public void OfFiles_of_a_video_file_the_client_does_not_say_it_downloads_is_the_owners_review()
    {
        //Arrange
        ClientJobFile[] files = [new("Alien.1979.mkv", 1_000, 1, null)];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Review, "“qBittorrent” doesn't say whether it downloads “Alien.1979.mkv”, so the app can't confirm the file is complete."),
            (result.Status, result.Problem));
    }

    [Fact]
    public void OfFiles_of_a_wanted_video_file_without_a_progress_is_the_owners_review()
    {
        //Arrange
        ClientJobFile[] files = [new("Alien.1979.mkv", 1_000, null, true)];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Review,
                "“qBittorrent” doesn't say how much of “Alien.1979.mkv” it has downloaded, so the app can't confirm the file is complete."),
            (result.Status, result.Problem));
    }

    [Fact]
    public void OfFiles_says_nothing_of_a_skipped_or_companion_file_short_of_complete()
    {
        //Arrange
        ClientJobFile[] files =
        [
            new("Alien.1979.mkv", 1_000, 1, true),
            new("Alien.1979.Extras.mkv", 1_000, 0, false),
            new("Alien.1979.srt", 10, 0.5, true),
            new("Alien.1979.nfo", 1, null, null),
        ];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        Assert.Equal(NewFileCompletenessStatus.Complete, result.Status);
        Assert.Equal([0], result.NewFiles.Select(file => file.Position));
    }

    [Fact]
    public void OfFiles_of_a_download_with_no_video_file_it_downloads_is_the_owners_review()
    {
        //Arrange
        ClientJobFile[] files = [new("Alien.1979.mkv", 1_000, 0, false), new("Alien.1979.srt", 10, 1, true)];

        //Act
        var result = NewFileCompleteness.OfFiles(files, Client);

        //Assert
        Assert.Equal(
            (NewFileCompletenessStatus.Review, "The download holds no video file that “qBittorrent” downloads, so nothing can replace the old copies."),
            (result.Status, result.Problem));
    }

    [Fact]
    public void OfFiles_of_a_download_whose_files_are_not_listed_yet_waits()
    {
        //Arrange

        //Act
        var result = NewFileCompleteness.OfFiles([], Client);

        //Assert
        // A magnet's metadata still being fetched: the client lists no file yet.
        Assert.Equal(
            (NewFileCompletenessStatus.Wait, "“qBittorrent” hasn't listed this download's files, so they can't be checked."),
            (result.Status, result.Problem));
    }

    [Fact]
    public void OfJob_names_the_client_by_its_integrations_name_cleaned_for_a_sentence()
    {
        //Arrange
        // An integration's name is the owner's; a line break in it would split the sentence, an override reverse it.
        const string name = "NAS\nclient‮";

        //Act
        var result = NewFileCompleteness.OfJob(null, name);

        //Assert
        Assert.Equal("“NAS client” no longer has this download, so the app can't confirm its files are complete.", result.Problem);
    }

    private static ClientJob Job(
        ClientJobState state, double? progress, string? savePath = "/downloads/movies", string? contentPath = "/downloads/movies/Alien.1979.mkv") =>
        new("abc", "Alien.1979.mkv", state, progress, "movies", contentPath, ["abc"], savePath);
}
