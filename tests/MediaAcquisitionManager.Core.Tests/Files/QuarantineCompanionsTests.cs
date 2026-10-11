using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Files;

// MAM-26, ADR 0030 "What" — which files beside an old video move into the quarantine with it: each file named after the
// video without its extension followed by a dot (Alien.srt, Alien.uk.srt, Alien.nfo beside Alien.mkv) that is no media
// file itself, and that doesn't begin with the longer name of another video in the folder followed by a dot — beside
// Alien (1979).mkv and Alien (1979).2160p.mkv, Alien (1979).2160p.uk.srt belongs to the second and stays. Never the video
// itself, never another video. Letter case follows the folder's path style. The names are spelled out here.
public sealed class QuarantineCompanionsTests
{
    [Fact]
    public void Pick_takes_the_videos_own_companions_in_the_order_given()
    {
        //Arrange
        string[] folder = ["Alien.nfo", "Alien.mkv", "Alien.srt", "Alien.uk.srt", "poster.jpg"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien.mkv", folder, PathStyle.Posix);

        //Assert
        Assert.Equal(["Alien.nfo", "Alien.srt", "Alien.uk.srt"], companions);
    }

    [Fact]
    public void Pick_leaves_a_companion_of_another_video_whose_name_begins_with_the_videos()
    {
        //Arrange
        // qBittorrent saves a single-file torrent straight into the library folder: two films of one name side by side.
        string[] folder = ["Alien (1979).mkv", "Alien (1979).2160p.mkv", "Alien (1979).srt", "Alien (1979).2160p.uk.srt"];

        //Act
        var ofTheFirst = QuarantineCompanions.Pick("Alien (1979).mkv", folder, PathStyle.Posix);
        var ofTheSecond = QuarantineCompanions.Pick("Alien (1979).2160p.mkv", folder, PathStyle.Posix);

        //Assert
        // The 2160p subtitle begins with the second video's longer name and a dot, so it belongs to that one and stays.
        Assert.Equal(["Alien (1979).srt"], ofTheFirst);
        Assert.Equal(["Alien (1979).2160p.uk.srt"], ofTheSecond);
    }

    [Fact]
    public void Pick_never_takes_the_video_itself_nor_another_video()
    {
        //Arrange
        string[] folder = ["Alien.mkv", "Alien.sample.mkv", "Alien.trailer.mp4", "Alien.iso", "Alien.srt"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien.mkv", folder, PathStyle.Posix);

        //Assert
        // Every one of them begins with "Alien.", but a media file is a video of its own, and never moves as a companion.
        Assert.Equal(["Alien.srt"], companions);
    }

    [Fact]
    public void Pick_takes_no_name_without_the_videos_name_and_a_dot()
    {
        //Arrange
        string[] folder = ["Alien.mkv", "Aliens.srt", "Alien-commentary.srt", "Alien", "Alien.", "Alien (1979).srt", "subs.srt"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien.mkv", folder, PathStyle.Posix);

        //Assert
        // "Alien." is the name and the dot with nothing after it: no companion.
        Assert.Empty(companions);
    }

    [Fact]
    public void Pick_on_a_posix_folder_takes_the_videos_letter_case_only()
    {
        //Arrange
        string[] folder = ["Alien.mkv", "alien.srt", "ALIEN.nfo", "Alien.srt"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien.mkv", folder, PathStyle.Posix);

        //Assert
        // ext4 compares names byte for byte: alien.srt is another name's file there.
        Assert.Equal(["Alien.srt"], companions);
    }

    [Fact]
    public void Pick_on_a_windows_folder_ignores_letter_case()
    {
        //Arrange
        string[] folder = ["Alien.mkv", "alien.srt", "ALIEN.nfo", "Alien.srt"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien.mkv", folder, PathStyle.Windows);

        //Assert
        Assert.Equal(["alien.srt", "ALIEN.nfo", "Alien.srt"], companions);
    }

    [Fact]
    public void Pick_on_a_posix_folder_tells_another_videos_longer_name_by_its_letter_case()
    {
        //Arrange
        string[] folder = ["Alien (1979).mkv", "ALIEN (1979).2160P.mkv", "Alien (1979).2160p.uk.srt"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien (1979).mkv", folder, PathStyle.Posix);

        //Assert
        // On ext4 the other video's name in upper case is another name, so the subtitle isn't that video's: it is this one's.
        Assert.Equal(["Alien (1979).2160p.uk.srt"], companions);
    }

    [Fact]
    public void Pick_on_a_windows_folder_takes_another_videos_longer_name_in_any_letter_case()
    {
        //Arrange
        string[] folder = ["Alien (1979).mkv", "ALIEN (1979).2160P.mkv", "Alien (1979).2160p.uk.srt"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien (1979).mkv", folder, PathStyle.Windows);

        //Assert
        // On NTFS it is the same name, so the subtitle is the other video's and stays.
        Assert.Empty(companions);
    }

    [Fact]
    public void Pick_of_a_folder_holding_only_the_video_is_none()
    {
        //Arrange
        string[] folder = ["Alien.mkv"];

        //Act
        var companions = QuarantineCompanions.Pick("Alien.mkv", folder, PathStyle.Posix);

        //Assert
        Assert.Empty(companions);
    }
}
