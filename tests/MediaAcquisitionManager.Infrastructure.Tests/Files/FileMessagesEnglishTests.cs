using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-47 (ADR 0042) — the sentences of FileInventoryMessages and FileQuarantineMessages no test reaches through a real
// file system (a link whose target can't be followed, a time a DateTime can't hold, a kernel without statx's mount id, a
// file system without inodes or without a no-replace rename, an errno nobody can provoke, an ffprobe that answers
// nothing), rendered through their keys and asserting the English the producers wrote before it moved into files.json:
// a slip in a template can't change what the owner reads unseen.
public sealed class FileMessagesEnglishTests
{
    private const string Film = "/media/movies/Alien (1979)/Alien.mkv";

    [Theory]
    [InlineData("unfollowable-link", "“/media/movies/Alien (1979)/Alien.mkv” leads through a link to “//server/share/Alien.mkv”, which the app doesn't follow.")]
    [InlineData("time-out-of-range", "The app couldn't read “/media/movies/Alien (1979)/Alien.mkv”: its modification time is outside the years 1 to 9999.")]
    [InlineData("time-past-9999", "The app couldn't read “/media/movies/Alien (1979)/Alien.mkv”: its modification time is past the year 9999.")]
    [InlineData("no-answer", "ffprobe couldn't read “/media/movies/Alien (1979)/Alien.mkv” as media: it gave no answer.")]
    [InlineData("not-json", "ffprobe couldn't read “/media/movies/Alien (1979)/Alien.mkv” as media: its answer wasn't the JSON ffprobe writes.")]
    [InlineData("not-beside-quarantine", "“/media/movies/Alien (1979)/Alien.mkv” isn't a file inside the media folder that holds the quarantine “/media/.mam-quarantine”.")]
    [InlineData("no-identity", "The file system holding “/media/movies/Alien (1979)/Alien.mkv” doesn't report a file's inode, so the app can't make sure it changes the file it checked.")]
    [InlineData("no-mount-id", "This system doesn't say which mount “/media/movies/Alien (1979)/Alien.mkv” is on (Linux 5.8 or later does), so the app can't make sure a rename stays on one mount.")]
    [InlineData("read-only", "“/media/movies/Alien (1979)/Alien.mkv” is on a read-only mount.")]
    [InlineData("rename-unsupported", "The file system holding “/media/movies/Alien (1979)/Alien.mkv” can't rename a file without replacing a name already there, so the app renames nothing on it.")]
    // The operating system's words keep their errno and lose their own full stop.
    [InlineData("could-not-change", "The app couldn't rename or delete “/media/movies/Alien (1979)/Alien.mkv”: Device or resource busy (errno 16).")]
    [InlineData("could-not-read", "The app couldn't read “/media/movies/Alien (1979)/Alien.mkv”: Input/output error.")]
    public void Message_no_file_system_here_reaches_reads_as_the_English_it_replaced(string sentence, string english)
    {
        //Arrange
        var message = sentence switch
        {
            "unfollowable-link" => FileInventoryMessages.UnfollowableLink(Film, "//server/share/Alien.mkv"),
            "time-out-of-range" => FileQuarantineMessages.TimeOutOfRange(Film),
            "time-past-9999" => FileInventoryMessages.TimePastYear9999(Film),
            "no-answer" => FileInventoryMessages.MediaUnreadable(Film, FileInventoryMessages.NoAnswer),
            "not-json" => FileInventoryMessages.MediaUnreadable(Film, FileInventoryMessages.NotJson),
            "not-beside-quarantine" => FileQuarantineMessages.NotBesideQuarantine(Film, "/media/.mam-quarantine"),
            "no-identity" => FileQuarantineMessages.NoIdentity(Film),
            "no-mount-id" => FileQuarantineMessages.NoMountId(Film),
            "read-only" => FileQuarantineMessages.ReadOnly(Film),
            "rename-unsupported" => FileQuarantineMessages.RenameUnsupported(Film),
            "could-not-change" => FileQuarantineMessages.CouldNotChange(Film, "Device or resource busy (errno 16)"),
            "could-not-read" => FileQuarantineMessages.CouldNotRead(Film, "Input/output error."),
            _ => throw new ArgumentOutOfRangeException(nameof(sentence), sentence, "No such sentence in this test."),
        };

        //Act
        var rendered = TestTexts.En(message);

        //Assert
        Assert.Equal(english, rendered);
    }

    [Fact]
    public void Reason_of_the_systems_words_is_them_cleaned_as_before_without_their_full_stops()
    {
        //Arrange
        // A line break and a right-to-left override as a hostile file system could report them, and a run of spaces.
        var said = "Input/output\nerror\u202E  here...";

        //Act
        var reason = FileInventoryMessages.Reason(said);

        //Assert
        Assert.Equal("Input/output error here", reason.Text);
    }
}
