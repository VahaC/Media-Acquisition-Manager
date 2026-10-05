using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33, ADR 0024 — a user's daily search limit split evenly over the tabs they have on, the remainder one each to
// the first tabs; a tab under its share served first, the furthest under first; a tab over it only once no tab
// under its share has anything due; nothing once the limit is spent.
public sealed class RecommendationSearchBudgetTests
{
    private static readonly RecommendationTab[] AllTabs =
        [RecommendationTab.Movies, RecommendationTab.Series, RecommendationTab.Cartoons, RecommendationTab.Documentaries];

    [Theory]
    [InlineData(60, new[] { 15, 15, 15, 15 })]
    [InlineData(62, new[] { 16, 16, 15, 15 })]
    [InlineData(3, new[] { 1, 1, 1, 0 })]
    [InlineData(0, new[] { 0, 0, 0, 0 })]
    public void ShareOf_splits_the_limit_evenly_with_the_remainder_to_the_first_tabs(int limit, int[] expected)
    {
        //Arrange

        //Act
        var shares = AllTabs.Select(tab => RecommendationSearchBudget.ShareOf(limit, AllTabs, tab)).ToArray();

        //Assert
        Assert.Equal(expected, shares);
    }

    [Fact]
    public void ShareOf_gives_a_tab_that_is_off_nothing_and_the_others_its_part()
    {
        //Arrange
        RecommendationTab[] tabs = [RecommendationTab.Series, RecommendationTab.Documentaries];

        //Act
        var shares = AllTabs.Select(tab => RecommendationSearchBudget.ShareOf(60, tabs, tab)).ToArray();

        //Assert
        Assert.Equal([0, 30, 0, 30], shares);
    }

    [Fact]
    public void NextTab_serves_the_tab_furthest_under_its_share_first()
    {
        //Arrange
        var used = new Dictionary<RecommendationTab, int> { [RecommendationTab.Movies] = 3, [RecommendationTab.Series] = 1 };

        //Act
        var next = RecommendationSearchBudget.NextTab(60, AllTabs, used, 4, [RecommendationTab.Movies, RecommendationTab.Series]);

        //Assert
        Assert.Equal(RecommendationTab.Series, next);
    }

    [Fact]
    public void NextTab_keeps_a_tab_at_its_share_waiting_while_another_tab_has_something_due()
    {
        //Arrange
        var used = new Dictionary<RecommendationTab, int> { [RecommendationTab.Movies] = 15, [RecommendationTab.Series] = 14 };

        //Act
        var next = RecommendationSearchBudget.NextTab(60, AllTabs, used, 29, [RecommendationTab.Movies, RecommendationTab.Series]);

        //Assert
        Assert.Equal(RecommendationTab.Series, next);
    }

    [Fact]
    public void NextTab_gives_the_unused_share_to_the_tabs_over_theirs_once_no_tab_under_its_share_has_anything_due()
    {
        //Arrange
        var used = new Dictionary<RecommendationTab, int> { [RecommendationTab.Movies] = 16, [RecommendationTab.Series] = 15 };

        //Act
        var next = RecommendationSearchBudget.NextTab(60, AllTabs, used, 31, [RecommendationTab.Movies, RecommendationTab.Series]);

        //Assert
        // Series is the least over its share; Cartoons and Documentaries, with nothing due, leave theirs.
        Assert.Equal(RecommendationTab.Series, next);
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(60, 75)]
    [InlineData(0, 0)]
    public void NextTab_is_none_once_the_limit_is_spent(int limit, int usedTotal)
    {
        //Arrange
        var used = new Dictionary<RecommendationTab, int>();

        //Act
        var next = RecommendationSearchBudget.NextTab(limit, AllTabs, used, usedTotal, AllTabs);

        //Assert
        Assert.Null(next);
    }

    [Fact]
    public void NextTab_is_none_when_no_tab_of_the_user_has_anything_due()
    {
        //Arrange
        RecommendationTab[] tabs = [RecommendationTab.Movies];

        //Act
        var next = RecommendationSearchBudget.NextTab(60, tabs, new Dictionary<RecommendationTab, int>(), 0, [RecommendationTab.Series]);

        //Assert
        Assert.Null(next);
    }
}
