using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — what a file's name says about the episodes it covers (ADR 0032, ADR 0035): hints that may only stop a
// replacement. The range is Jellyfin 12.2's own reading — its ten multi-episode expressions and its parser's rules — so
// the names below are its MultiEpisodeTests' seasoned ones, with the end number it reads (a name it reads none of gives no
// range here either); the half is MAM-37's segment letter, an "a" or a "b" only.
public sealed class EpisodeRangeNamesTests
{
    [Theory]
    // The forms of the design: every way of writing season 2, episodes 5 to 6.
    [InlineData("Show.S02E05-E06.mkv", 2, 5, 6)]
    [InlineData("Show.S02E05E06.mkv", 2, 5, 6)]
    [InlineData("Show.S02E05-06.mkv", 2, 5, 6)]
    [InlineData("Show.S02E05x06.mkv", 2, 5, 6)]
    [InlineData("Show.2x05-2x06.mkv", 2, 5, 6)]
    [InlineData("Show.2x05-06.mkv", 2, 5, 6)]
    [InlineData("show.s02e05-e06.mkv", 2, 5, 6)]
    [InlineData("Show S02E05 - E06.mkv", 2, 5, 6)]
    [InlineData("Show.S02.E05-E06.mkv", 2, 5, 6)]
    [InlineData("S02E05E06E07.mkv", 2, 5, 7)]
    // A path whose last part is the name.
    [InlineData("/media/tv-shows/Show/Season 02/Show.S02E05-E06.mkv", 2, 5, 6)]
    [InlineData(@"D:\TV\Show\Season 02\Show.S02E05-E06.mkv", 2, 5, 6)]
    // Jellyfin 12.2's MultiEpisodeTests, every name with a season it reads an end number of.
    [InlineData("Season 2/Elementary - 02x03 - 02x04 - 02x15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 2/02x03 - 02x04 - 02x15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 2/02x03-04-15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 2/Elementary - 02x03-04-15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 02/02x03-E15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 02/Elementary - 02x03-E15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 02/02x03 - x04 - x15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 02/Elementary - 02x03 - x04 - x15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 02/02x03x04x15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 02/Elementary - 02x03x04x15 - Ep Name.mp4", 2, 3, 15)]
    [InlineData("Season 1/Elementary - S01E23-E24-E26 - The Woman.mp4", 1, 23, 26)]
    [InlineData("Season 1/S01E23-E24-E26 - The Woman.mp4", 1, 23, 26)]
    [InlineData("Season 2009/Elementary - 2009x03 - 2009x04 - 2009x15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/2009x03 - 2009x04 - 2009x15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/2009x03-04-15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/Elementary - 2009x03-04-15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/2009x03-E15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/Elementary - 2009x03-E15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/2009x03 - x04 - x15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/Elementary - 2009x03 - x04 - x15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/2009x03x04x15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/Elementary - 2009x03x04x15 - Ep Name.mp4", 2009, 3, 15)]
    [InlineData("Season 2009/Elementary - S2009E23-E24-E26 - The Woman.mp4", 2009, 23, 26)]
    [InlineData("Season 2009/S2009E23-E24-E26 - The Woman.mp4", 2009, 23, 26)]
    [InlineData("Season 1/MOONLIGHTING_s01e01-e04.mkv", 1, 1, 4)]
    [InlineData("Season 1/MOONLIGHTING_s01e01-e04", 1, 1, 4)]
    public void RangeOf_a_name_jellyfin_reads_a_range_of_is_that_range(string fileName, int season, int first, int last)
    {
        //Arrange

        //Act
        var range = EpisodeRangeNames.RangeOf(fileName);

        //Assert
        Assert.Equal(new EpisodeNameRange(season, first, last), range);
        Assert.True(range!.CoversSeveral);
    }

    [Theory]
    // Jellyfin 12.2's MultiEpisodeTests, every name with a season it reads no end number of.
    [InlineData("Season 1/4x01 – 20 Hours in America (1).mkv")]
    [InlineData("Season 1/01x02 blah.avi")]
    [InlineData("Season 1/S01x02 blah.avi")]
    [InlineData("Season 1/S01E02 blah.avi")]
    [InlineData("Season 1/S01xE02 blah.avi")]
    [InlineData("Season 1/seriesname 01x02 blah.avi")]
    [InlineData("Season 1/seriesname S01x02 blah.avi")]
    [InlineData("Season 1/seriesname S01E02 blah.avi")]
    [InlineData("Season 1/seriesname S01xE02 blah.avi")]
    [InlineData("Season 2/02x03 - 04 Ep Name.mp4")]
    [InlineData("Season 2/My show name 02x03 - 04 Ep Name.mp4")]
    [InlineData("Season 2009/2009x02 blah.avi")]
    [InlineData("Season 2009/S2009x02 blah.avi")]
    [InlineData("Season 2009/S2009E02 blah.avi")]
    [InlineData("Season 2009/S2009xE02 blah.avi")]
    [InlineData("Season 2009/seriesname 2009x02 blah.avi")]
    [InlineData("Season 2009/seriesname S2009x02 blah.avi")]
    [InlineData("Season 2009/seriesname S2009E02 blah.avi")]
    [InlineData("Season 2009/seriesname S2009xE02 blah.avi")]
    // A resolution after the dash is no end number.
    [InlineData("Season 1/series-s09e14-1080p.mkv")]
    [InlineData("Season 1/series-s09e14-720p.mkv")]
    [InlineData("Season 1/series-s09e14-720i.mkv")]
    // Hyphenated numbers in the episode's title are no range.
    [InlineData("Season 1/S01E01 The 6-10 to Lubbock [WEBRip-1080p][AV1 Opus].mkv")]
    [InlineData("Season 5/S05E23 11-59 [HDTV-1080p][x265 AC3].mkv")]
    [InlineData("Season 5/S05E23 11-59 [HDTV-1080p][HEVC AC3].mkv")]
    [InlineData("Season 1/S01E01 1-23-45 [Bluray-1080p][AV1 Opus].mkv")]
    // An end before the start belongs to the episode's title.
    [InlineData("Season 03/Star Trek Enterprise (2001) - S03E21 - E2 (1080p BluRay x265).mkv")]
    [InlineData("Season 02/Series Name (2001) - S02E10 - E5 [WEBRip-1080p].mkv")]
    public void RangeOf_a_name_jellyfin_reads_no_range_of_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var range = EpisodeRangeNames.RangeOf(fileName);

        //Assert
        Assert.Null(range);
    }

    [Theory]
    // Jellyfin 12.2 reads none of these as a range either: "S02E05xE06" is no form of its expressions (its "S01xE02" is
    // one episode), and a repeated season is never one. A file of them that Jellyfin numbers as one episode passes as one.
    [InlineData("Show.S02E05xE06.mkv")]
    [InlineData("Show.S02E05-S02E06.mkv")]
    [InlineData("Show - S02E05 - S02E06.mkv")]
    // Episodes without a season are read by other expressions of Jellyfin's; a replacement compares within a season.
    [InlineData("Season 1/02-03 - blah.avi")]
    [InlineData("Episode 16-17.mkv")]
    // A season from 200 to 1927, or past 2500, is a picture's size.
    [InlineData("Show.S300E01-E02.mkv")]
    [InlineData("Show.S2600E01-E02.mkv")]
    [InlineData("Series Special (1920x1080).mkv")]
    [InlineData("Show.S03E09a.mkv")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RangeOf_a_name_with_no_seasoned_range_jellyfin_reads_is_nothing(string? fileName)
    {
        //Arrange

        //Act
        var range = EpisodeRangeNames.RangeOf(fileName);

        //Assert
        Assert.Null(range);
    }

    [Fact]
    public void RangeOf_a_range_of_one_episode_covers_one()
    {
        //Arrange

        //Act
        var range = EpisodeRangeNames.RangeOf("Show.S02E05-E05.mkv");

        //Assert
        // Jellyfin keeps an end equal to the start; it names one episode.
        Assert.Equal(new EpisodeNameRange(2, 5, 5), range);
        Assert.False(range!.CoversSeveral);
    }

    [Theory]
    [InlineData("Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv", 'a')]
    [InlineData("Mickey Mouse Clubhouse_S03E09b_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv", 'b')]
    [InlineData("S03E09a", 'a')]
    [InlineData("s03e09b.mkv", 'b')]
    [InlineData("/media/tv-shows/Mickey/Season 03/Show_S03E09a_1080p.mkv", 'a')]
    public void HalfOf_a_name_whose_first_marker_carries_an_a_or_a_b_is_that_half(string fileName, char half)
    {
        //Arrange

        //Act
        var read = EpisodeRangeNames.HalfOf(fileName);

        //Assert
        Assert.Equal(half, read);
    }

    [Theory]
    [InlineData("S03E09ab.mkv")]
    [InlineData("S03E09.avi")]
    [InlineData("S03E09a-b.mkv")]
    [InlineData("S03E09A.mkv")]
    [InlineData("S01E05A Day at the Beach.mkv")]
    [InlineData("S03E09 a.mkv")]
    // A segment past "b": MAM-37 reads it, a half it is not.
    [InlineData("S03E09c.mkv")]
    [InlineData("S01E01E02.mkv")]
    [InlineData("Alien.1979.mkv")]
    [InlineData("")]
    [InlineData(null)]
    public void HalfOf_any_other_name_is_nothing(string? fileName)
    {
        //Arrange

        //Act
        var read = EpisodeRangeNames.HalfOf(fileName);

        //Assert
        Assert.Null(read);
    }
}
