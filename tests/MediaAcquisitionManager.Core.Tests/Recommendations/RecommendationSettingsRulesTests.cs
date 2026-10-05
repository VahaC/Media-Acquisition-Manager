using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33, Vasyl's decision 5 — a user's recommendation settings: every tab on and 60 searches a day until they save
// (decided 2026-10-05), a daily limit from 0 to 500, and the tabs a saved row has on.
public sealed class RecommendationSettingsRulesTests
{
    [Fact]
    public void Default_has_every_tab_on_and_sixty_searches_a_day()
    {
        //Arrange

        //Act
        var settings = RecommendationSettingsRules.Default;

        //Assert
        Assert.Equal(new RecommendationSettings(true, true, true, true, 60), settings);
        Assert.Equal(
            [RecommendationTab.Movies, RecommendationTab.Series, RecommendationTab.Cartoons, RecommendationTab.Documentaries],
            settings.Tabs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(500)]
    public void ValidateDailySearchLimit_accepts_a_whole_number_from_0_to_500(int limit)
    {
        //Arrange

        //Act
        var error = RecommendationSettingsRules.ValidateDailySearchLimit(limit);

        //Assert
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(501)]
    [InlineData(int.MaxValue)]
    public void ValidateDailySearchLimit_refuses_a_missing_or_out_of_range_limit_with_the_sentence(int? limit)
    {
        //Arrange

        //Act
        var error = RecommendationSettingsRules.ValidateDailySearchLimit(limit);

        //Assert
        Assert.Equal("The daily search limit is a whole number from 0 to 500.", error);
    }

    [Fact]
    public void Tabs_of_saved_settings_are_the_ones_on_in_the_pages_order()
    {
        //Arrange
        var settings = new RecommendationSettings(Movies: false, Series: true, Cartoons: false, Documentaries: true, DailySearchLimit: 0);

        //Act
        var tabs = settings.Tabs;

        //Assert
        Assert.Equal([RecommendationTab.Series, RecommendationTab.Documentaries], tabs);
        Assert.False(settings.IsOn(RecommendationTab.Movies));
        Assert.True(settings.IsOn(RecommendationTab.Documentaries));
    }
}
