using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34 — the season and episode a title or a file name marks, which prefill an episode-to-show suggestion: a hint the
// owner confirms (ADR 0017), read from S01E08 and E08 markers standing on their own, and from nothing else.
public sealed class EpisodeMarkerTests
{
    [Theory]
    [InlineData("Yellowstone S01E08 1080p", 1, 8)]
    [InlineData("yellowstone.s1e8.mkv", 1, 8)]
    [InlineData("Planet Earth S01 E08", 1, 8)]
    [InlineData("Specials S00E01", 0, 1)]
    [InlineData("Show S123E1234", 123, 1234)]
    public void Read_takes_the_season_and_the_episode_of_a_full_marker(string text, int season, int episode)
    {
        //Arrange

        //Act
        var marker = EpisodeMarker.Read(text);

        //Assert
        Assert.Equal(((int?)season, episode), marker);
    }

    [Theory]
    [InlineData("Planet_Earth_E08 - Jungles", 8)]
    [InlineData("Planet Earth E08", 8)]
    [InlineData("Planet.Earth.E08.Jungles.mkv", 8)]
    [InlineData("E11 Ocean Deep", 11)]
    public void Read_takes_the_episode_of_an_episode_marker_and_no_season(string text, int episode)
    {
        //Arrange

        //Act
        var marker = EpisodeMarker.Read(text);

        //Assert
        Assert.Equal(((int?)null, episode), marker);
    }

    [Fact]
    public void Read_takes_the_first_marker()
    {
        //Arrange

        //Act
        var marker = EpisodeMarker.Read("Show S02E03 (was S01E09)");

        //Assert
        Assert.Equal(((int?)2, 3), marker);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("120. Landing Stripling (1962)")]
    [InlineData("THE08 Something")]
    [InlineData("Sequel2E3")]
    [InlineData("Planet Earth E08x")]
    [InlineData("Episode 8")]
    [InlineData("Planet Earth E00")]
    [InlineData("Planet Earth E٠٨")]
    public void Read_of_a_text_with_no_marker_of_its_own_is_nothing(string? text)
    {
        //Arrange

        //Act
        var marker = EpisodeMarker.Read(text);

        //Assert
        Assert.Null(marker);
    }
}
