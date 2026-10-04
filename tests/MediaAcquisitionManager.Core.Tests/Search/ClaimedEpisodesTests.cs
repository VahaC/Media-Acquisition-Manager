using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-32 — whether a library episode is among those a TV release's title claims: what the held count of a
// TV release's match counts. A season or a number the library doesn't know never counts.
public sealed class ClaimedEpisodesTests
{
    private static readonly ClaimedEpisodes SeasonsOneToThree = new(1, 3, null, null);

    private static readonly ClaimedEpisodes EpisodesTwoToFourOfSeasonOne = new(1, 1, 2, 4);

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(3, 12, true)]
    // Whole seasons: an episode without a number is in its season.
    [InlineData(2, null, true)]
    [InlineData(0, 1, false)]
    [InlineData(4, 1, false)]
    [InlineData(null, 1, false)]
    public void Holds_of_seasons_takes_every_episode_of_each(int? season, int? episode, bool expected)
    {
        //Arrange

        //Act
        var holds = SeasonsOneToThree.Holds(season, episode);

        //Assert
        Assert.Equal(expected, holds);
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(1, 4, true)]
    [InlineData(1, 1, false)]
    [InlineData(1, 5, false)]
    // An episode without a number may be any of the season's.
    [InlineData(1, null, false)]
    [InlineData(2, 3, false)]
    [InlineData(null, 3, false)]
    public void Holds_of_episodes_takes_those_of_their_season_alone(int? season, int? episode, bool expected)
    {
        //Arrange

        //Act
        var holds = EpisodesTwoToFourOfSeasonOne.Holds(season, episode);

        //Assert
        Assert.Equal(expected, holds);
    }

    [Fact]
    public void Holds_of_the_specials_takes_season_zero()
    {
        //Arrange
        var specials = new ClaimedEpisodes(0, 0, null, null);

        //Act
        var holds = (specials.Holds(0, 3), specials.Holds(1, 3));

        //Assert
        Assert.Equal((true, false), holds);
    }
}
