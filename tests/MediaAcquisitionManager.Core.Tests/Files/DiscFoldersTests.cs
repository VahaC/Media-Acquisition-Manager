using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Files;

// MAM-26, ADR 0030 — a clip of a Blu-ray or DVD folder is never moved one file at a time: the app knows one clip of a disc
// (BDMV/STREAM/00000.m2ts, VIDEO_TS/VTS_01_1.VOB), and moving it alone would break the disc. Told by a BDMV or VIDEO_TS
// folder anywhere on the way to the file, letter case ignored, as discs are authored in upper case and a copied one keeps
// it on any file system; a file of that name, or a folder that only begins with it, is no disc.
public sealed class DiscFoldersTests
{
    [Theory]
    [InlineData("/media/movies/Alien (1979)/BDMV/STREAM/00000.m2ts")]
    [InlineData("/media/movies/Alien (1979)/VIDEO_TS/VTS_01_1.VOB")]
    [InlineData("/media/movies/Alien (1979) Blu-ray/Disc 1/BDMV/STREAM/00001.m2ts")]
    [InlineData("/media/movies/Alien (1979)/bdmv/stream/00000.m2ts")]
    [InlineData("/media/movies/Alien (1979)/Video_TS/VTS_01_1.vob")]
    [InlineData(@"D:\media\movies\Alien (1979)\BDMV\STREAM\00000.m2ts")]
    public void HoldsClip_of_a_file_inside_a_disc_folder_is_true(string path)
    {
        //Arrange
        var parsed = PathOf(path);

        //Act
        var holdsClip = DiscFolders.HoldsClip(parsed);

        //Assert
        Assert.True(holdsClip);
    }

    [Theory]
    [InlineData("/media/movies/Alien (1979)/Alien (1979).mkv")]
    [InlineData("/media/movies/Alien (1979)/BDMV")]
    [InlineData("/media/movies/Alien (1979)/VIDEO_TS.mkv")]
    [InlineData("/media/movies/BDMV2/STREAM/00000.m2ts")]
    [InlineData("/media/movies/Alien BDMV Remux/Alien.mkv")]
    public void HoldsClip_of_a_file_in_no_disc_folder_is_false(string path)
    {
        //Arrange
        var parsed = PathOf(path);

        //Act
        var holdsClip = DiscFolders.HoldsClip(parsed);

        //Assert
        Assert.False(holdsClip);
    }

    [Theory]
    [InlineData("BDMV", true)]
    [InlineData("bdmv", true)]
    [InlineData("VIDEO_TS", true)]
    [InlineData("Video_TS", true)]
    [InlineData("BDMV2", false)]
    [InlineData("STREAM", false)]
    [InlineData("VIDEO_TS.mkv", false)]
    public void IsDiscFolderName_is_true_for_bdmv_and_video_ts_in_any_letter_case(string name, bool expected)
    {
        //Arrange

        //Act
        var isDiscFolder = DiscFolders.IsDiscFolderName(name);

        //Assert
        Assert.Equal(expected, isDiscFolder);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static NormalizedPath PathOf(string text)
    {
        Assert.True(NormalizedPath.TryParse(text, out var path, out _), $"“{text}” doesn't parse.");
        return path;
    }
}
