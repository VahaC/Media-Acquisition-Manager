using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — check 2 of a replacement's new file (ADR 0035): the app's own look at it. Failed only for a bad file — not a
// regular file, unreadable as media, empty, no video stream, no running time (Vasyl's default, 2026-10-10); a file missing,
// outside the allowed folders or unreadable to the app, one without an identity, one of another size than the client
// downloaded and one that is an old copy itself are the owner's review; a file changing while read and an inspector that
// can't start are a wait. The inventory's own sentence is the reason when it has one.
public sealed class NewFileReadabilityTests
{
    private const string NewPath = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";
    private const long Size = 3_891_719_630;

    private static readonly TickedCopyIdentity OldCopy = new("/media/movies/Alien (1979)/Alien.1979.1080p.mkv", TestMedia.DeviceId, 1);

    [Fact]
    public void Check_of_a_regular_media_file_with_video_and_a_running_time_of_the_clients_size_is_readable()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: Size), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal((NewFileReadabilityStatus.Readable, (string?)null), (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_a_file_the_client_gave_no_size_for_is_not_compared_by_size()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: 12), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, null, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(NewFileReadabilityStatus.Readable, result.Status);
    }

    [Theory]
    [InlineData(FileInventoryStatus.NotFound, "Nothing is at “/media/movies/Alien (1979)/Alien.1979.2160p.mkv”.")]
    [InlineData(FileInventoryStatus.OutsideAllowedRoots, "“/media/movies/Alien (1979)/Alien.1979.2160p.mkv” is outside the media folders the app may read.")]
    [InlineData(FileInventoryStatus.Unreadable, "The app isn't allowed to read “/media/movies/Alien (1979)/Alien.1979.2160p.mkv”.")]
    public void Check_of_a_file_the_app_can_not_reach_is_the_owners_review_with_the_inventorys_sentence(FileInventoryStatus status, string sentence)
    {
        //Arrange
        var inspection = new FileInventoryResult<MediaInspection>(status, null, Message.Legacy(sentence));

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        // It may come right: a mount back, a permission given, the folder allowed.
        Assert.Equal((NewFileReadabilityStatus.Review, sentence), (result.Status, TestTexts.En(result.Problem)));
    }

    [Theory]
    [InlineData(FileInventoryStatus.ChangedWhileReading)]
    [InlineData(FileInventoryStatus.InspectorUnavailable)]
    public void Check_of_a_file_changing_while_read_or_without_an_inspector_waits(FileInventoryStatus status)
    {
        //Arrange
        var inspection = new FileInventoryResult<MediaInspection>(status, null, Message.Legacy("Why."));

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal((NewFileReadabilityStatus.Wait, "Why."), (result.Status, TestTexts.En(result.Problem)));
    }

    [Theory]
    [InlineData(FileInventoryStatus.MediaUnreadable)]
    [InlineData(FileInventoryStatus.NotARegularFile)]
    public void Check_of_a_file_that_is_no_media_file_failed(FileInventoryStatus status)
    {
        //Arrange
        var inspection = new FileInventoryResult<MediaInspection>(status, null, Message.Legacy("ffprobe couldn't read it as media: invalid data."));

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal((NewFileReadabilityStatus.Failed, "ffprobe couldn't read it as media: invalid data."), (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_a_failure_without_a_sentence_says_the_app_could_not_read_it()
    {
        //Arrange
        var inspection = new FileInventoryResult<MediaInspection>(FileInventoryStatus.NotFound, null, null);

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal("The app couldn't read “/media/movies/Alien (1979)/Alien.1979.2160p.mkv”.", TestTexts.En(result.Problem));
    }

    [Fact]
    public void Check_of_a_file_without_an_identity_is_the_owners_review()
    {
        //Arrange
        var inspection = Found(new FileFacts(Size, TestMedia.ModifiedUtc, null, null, null), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(
            (NewFileReadabilityStatus.Review,
                "The app can't read the identity of “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” on this platform, so it can't tell the new file from the old copies."),
            (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_an_empty_file_failed()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: 0), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, 0, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal((NewFileReadabilityStatus.Failed, "“/media/movies/Alien (1979)/Alien.1979.2160p.mkv” is empty."), (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_a_file_without_a_video_stream_failed()
    {
        //Arrange
        var media = TestMedia.Taken() with { Tracks = [.. TestMedia.Taken().Tracks.Where(track => track.Kind != MediaTrackKind.Video)] };
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: Size), media);

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(
            (NewFileReadabilityStatus.Failed, "“/media/movies/Alien (1979)/Alien.1979.2160p.mkv” has no video stream."), (result.Status, TestTexts.En(result.Problem)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Check_of_a_file_without_a_running_time_failed(long? milliseconds)
    {
        //Arrange
        var media = TestMedia.Taken() with { Duration = milliseconds is { } value ? TimeSpan.FromMilliseconds(value) : null };
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: Size), media);

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(
            (NewFileReadabilityStatus.Failed, "The media inspector found no running time in “/media/movies/Alien (1979)/Alien.1979.2160p.mkv”."),
            (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_a_file_of_another_size_than_the_client_downloaded_is_the_owners_review()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: 1_234_567), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, 7_654_321, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(
            (NewFileReadabilityStatus.Review,
                "“/media/movies/Alien (1979)/Alien.1979.2160p.mkv” is 1,234,567 bytes on the disk, not the 7,654,321 bytes “qBittorrent” downloaded, so it may be another file."),
            (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_a_new_file_that_is_an_old_copys_inode_is_the_owners_review()
    {
        //Arrange
        // Another torrent wrote at the new path, through a hard link to the old copy: ADR 0034's gap.
        var inspection = Found(TestMedia.File(inode: 1, sizeBytes: Size), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(
            (NewFileReadabilityStatus.Review, "The new file “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” is the old copy itself."),
            (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_of_a_new_file_at_an_old_copys_path_is_the_owners_review_even_with_another_identity()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: Size), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(OldCopy.AppPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(
            (NewFileReadabilityStatus.Review, "The new file “/media/movies/Alien (1979)/Alien.1979.1080p.mkv” is the old copy itself."),
            (result.Status, TestTexts.En(result.Problem)));
    }

    [Fact]
    public void Check_compares_a_windows_path_with_an_old_copys_in_any_letter_case()
    {
        //Arrange
        var oldCopy = new TickedCopyIdentity(@"D:\Media\Alien.mkv", TestMedia.DeviceId, 1);
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: Size), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(@"D:\media\ALIEN.mkv", inspection, Size, [oldCopy], "qBittorrent");

        //Assert
        Assert.Equal(NewFileReadabilityStatus.Review, result.Status);
    }

    [Fact]
    public void Check_of_an_old_copy_on_another_device_with_the_same_inode_is_another_file()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 1, sizeBytes: Size, deviceId: 42), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(NewFileReadabilityStatus.Readable, result.Status);
    }

    [Fact]
    public void Check_says_an_empty_file_failed_before_it_says_its_size_differs()
    {
        //Arrange
        var inspection = Found(TestMedia.File(inode: 2, sizeBytes: 0), TestMedia.Taken());

        //Act
        var result = NewFileReadability.Check(NewPath, inspection, Size, [OldCopy], "qBittorrent");

        //Assert
        Assert.Equal(NewFileReadabilityStatus.Failed, result.Status);
    }

    private static FileInventoryResult<MediaInspection> Found(FileFacts file, MediaFacts media) =>
        new(FileInventoryStatus.Found, new MediaInspection(file, media), null);
}
