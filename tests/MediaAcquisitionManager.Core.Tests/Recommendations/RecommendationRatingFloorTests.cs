using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33 — the Bayesian rating floor: an average pulled toward 5.0 as if 50 more people rated it so must still reach
// 6.0, which is votes × (rating − 6.0) ≥ 50.
public sealed class RecommendationRatingFloorTests
{
    [Theory]
    [InlineData(7.0, 50, true)]
    [InlineData(7.0, 49, false)]
    [InlineData(6.5, 100, true)]
    [InlineData(6.5, 99, false)]
    [InlineData(8.0, 25, true)]
    [InlineData(8.0, 24, false)]
    [InlineData(10.0, 13, true)]
    [InlineData(9.5, 4, false)]
    [InlineData(6.0, 1_000_000, false)]
    [InlineData(5.9, 100_000, false)]
    public void Passes_only_a_work_whose_adjusted_average_reaches_six(double rating, int votes, bool passes)
    {
        //Arrange

        //Act
        var passed = RecommendationRatingFloor.Passes(rating, votes);

        //Assert
        Assert.Equal(passes, passed);
    }

    [Theory]
    [InlineData(null, 1000)]
    [InlineData(9.0, 0)]
    public void Passes_never_a_work_nobody_rated(double? rating, int votes)
    {
        //Arrange

        //Act
        var passed = RecommendationRatingFloor.Passes(rating, votes);

        //Assert
        Assert.False(passed);
    }

    [Theory]
    [InlineData(10.0, 3, 5.283018868)]
    [InlineData(7.0, 50, 6.0)]
    [InlineData(8.5, 4950, 8.465)]
    [InlineData(null, 100, 5.0)]
    [InlineData(9.0, 0, 5.0)]
    public void WeightedRating_pulls_the_average_toward_five_as_if_fifty_more_rated_it_so(double? rating, int votes, double expected)
    {
        //Arrange

        //Act
        var weighted = RecommendationRatingFloor.WeightedRating(rating, votes);

        //Assert
        // A 10.0 of three votes weighs less than an 8.5 of thousands; a work nobody rated is the prior.
        Assert.Equal(expected, weighted, 6);
    }
}
