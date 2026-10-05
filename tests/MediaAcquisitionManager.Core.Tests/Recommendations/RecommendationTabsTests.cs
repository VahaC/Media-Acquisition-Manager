using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33, Vasyl's decision 2 — the tab a work goes to by the roles of its genres (a documentary first, then an
// animation, else a film or a show), and which kinds of work a set of tabs asks the catalog for.
public sealed class RecommendationTabsTests
{
    [Theory]
    [InlineData(MediaWorkKind.Movie, new[] { CatalogGenreRole.Documentary, CatalogGenreRole.Animation }, RecommendationTab.Documentaries)]
    [InlineData(MediaWorkKind.Series, new[] { CatalogGenreRole.Animation, CatalogGenreRole.Documentary }, RecommendationTab.Documentaries)]
    [InlineData(MediaWorkKind.Movie, new[] { CatalogGenreRole.Other, CatalogGenreRole.Animation }, RecommendationTab.Cartoons)]
    [InlineData(MediaWorkKind.Series, new[] { CatalogGenreRole.Animation }, RecommendationTab.Cartoons)]
    [InlineData(MediaWorkKind.Movie, new[] { CatalogGenreRole.Other }, RecommendationTab.Movies)]
    [InlineData(MediaWorkKind.Movie, new CatalogGenreRole[0], RecommendationTab.Movies)]
    [InlineData(MediaWorkKind.Series, new[] { CatalogGenreRole.Other }, RecommendationTab.Series)]
    public void Classify_puts_a_documentary_first_then_an_animation_then_the_works_kind(
        MediaWorkKind kind, CatalogGenreRole[] roles, RecommendationTab expected)
    {
        //Arrange

        //Act
        var tab = RecommendationTabs.Classify(kind, roles);

        //Assert
        Assert.Equal(expected, tab);
    }

    [Fact]
    public void Classify_of_an_episode_throws()
    {
        //Arrange

        //Act
        var exception = Record.Exception(() => RecommendationTabs.Classify(MediaWorkKind.Episode, []));

        //Assert
        // Only a film or a show is recommended; an episode reaching here is a caller's mistake.
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    [Theory]
    [InlineData(new[] { RecommendationTab.Movies }, true, false)]
    [InlineData(new[] { RecommendationTab.Series }, false, true)]
    [InlineData(new[] { RecommendationTab.Cartoons }, true, true)]
    [InlineData(new[] { RecommendationTab.Documentaries }, true, true)]
    [InlineData(new RecommendationTab[0], false, false)]
    public void Wants_asks_for_films_and_shows_only_as_the_tabs_hold_them(RecommendationTab[] tabs, bool films, bool shows)
    {
        //Arrange

        //Act
        var wanted = (RecommendationTabs.Wants(tabs, MediaWorkKind.Movie), RecommendationTabs.Wants(tabs, MediaWorkKind.Series));

        //Assert
        Assert.Equal((films, shows), wanted);
    }
}
