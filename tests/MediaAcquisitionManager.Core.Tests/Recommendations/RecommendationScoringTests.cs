using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33, ADR 0023 — the score: 8·MissingSeasons + 6·Collection + 3·(Director or Creator) + 2·log2(1 + recommenders) +
// 1·NewAtSource + 0.5·TopRated + the genre and decade affinities + 1.5·(weighted rating − 6), each part counted once.
// Set after the first run on the reference library: a well-rated part of a collection must not drown under a middling
// film many library works recommend, and more recommenders must still count past ten.
public sealed class RecommendationScoringTests
{
    [Fact]
    public void Score_adds_each_reasons_weight_once_the_affinities_and_the_weighted_rating()
    {
        //Arrange
        RecommendationReasonKind[] reasons =
        [
            RecommendationReasonKind.MissingSeasons,
            RecommendationReasonKind.Collection,
            RecommendationReasonKind.Director,
            RecommendationReasonKind.Creator,
            RecommendationReasonKind.Recommended,
            RecommendationReasonKind.NewAtSource,
            RecommendationReasonKind.TopRated,
        ];

        //Act
        var score = RecommendationScoring.Score(reasons, recommenders: 3, genreAffinity: 0.5, decadeAffinity: 0.25, rating: 8.0, voteCount: 950);

        //Assert
        // 8 + 6 + 3 (one person weight for Director and Creator) + 2·log2(4) + 1 + 0.5 + 0.5 + 0.25, and the 8.0 of 950
        // votes weighted to (950·8.0 + 50·5.0) / 1000 = 7.85: 1.5 × 1.85.
        Assert.Equal(26.025, score, 9);
    }

    [Theory]
    [InlineData(1, 2.0)]
    [InlineData(3, 4.0)]
    [InlineData(7, 6.0)]
    [InlineData(15, 8.0)]
    [InlineData(31, 10.0)]
    public void Score_adds_two_points_per_doubling_of_the_recommenders(int recommenders, double expected)
    {
        //Arrange
        // A 7.0 of 50 votes weighs exactly 6.0: the rating adds nothing.

        //Act
        var score = RecommendationScoring.Score([RecommendationReasonKind.Recommended], recommenders, 0, 0, rating: 7.0, voteCount: 50);

        //Assert
        Assert.Equal(expected, score, 9);
    }

    [Fact]
    public void Score_counts_no_recommenders_without_a_kept_recommended_reason()
    {
        //Arrange

        //Act
        var score = RecommendationScoring.Score([RecommendationReasonKind.Collection], recommenders: 7, 0, 0, rating: 7.0, voteCount: 50);

        //Assert
        Assert.Equal(6.0, score, 9);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(null, 400)]
    [InlineData(9.0, 0)]
    public void Score_weighs_a_work_nobody_rated_as_the_prior_five(double? rating, int voteCount)
    {
        //Arrange

        //Act
        var score = RecommendationScoring.Score([RecommendationReasonKind.Collection], recommenders: 0, 0, 0, rating, voteCount);

        //Assert
        // 6 for the collection, 1.5 × (5.0 − 6) for the rating.
        Assert.Equal(4.5, score, 9);
    }

    [Fact]
    public void Score_ranks_a_well_rated_collection_part_above_a_middling_film_fourteen_works_recommend()
    {
        //Arrange
        // The reference library's case: "2 of 3" of a collection, rated 7.9, against a 6.2 the catalog recommends beside
        // fourteen of the library's films.

        //Act
        var part = RecommendationScoring.Score([RecommendationReasonKind.Collection], recommenders: 0, 0.5, 0.5, rating: 7.9, voteCount: 5000);
        var middling = RecommendationScoring.Score([RecommendationReasonKind.Recommended], recommenders: 14, 0.5, 0.5, rating: 6.2, voteCount: 3000);

        //Assert
        Assert.True(part > middling, $"The collection's part scored {part}, the film fourteen works recommend {middling}.");
    }

    [Fact]
    public void Score_ranks_a_film_thirty_works_recommend_above_one_ten_recommend_at_the_same_rating()
    {
        //Arrange

        //Act
        var thirty = RecommendationScoring.Score([RecommendationReasonKind.Recommended], recommenders: 30, 0, 0, rating: 7.8, voteCount: 4000);
        var ten = RecommendationScoring.Score([RecommendationReasonKind.Recommended], recommenders: 10, 0, 0, rating: 7.8, voteCount: 4000);

        //Assert
        // The first formula's cap at ten levelled the two.
        Assert.True(thirty > ten + 2, $"Thirty recommenders scored {thirty}, ten {ten}.");
    }

    [Fact]
    public void Score_puts_missing_seasons_above_every_other_single_reason_at_the_same_rating()
    {
        //Arrange
        RecommendationReasonKind[] others =
        [
            RecommendationReasonKind.Collection,
            RecommendationReasonKind.Director,
            RecommendationReasonKind.Creator,
            RecommendationReasonKind.Recommended,
            RecommendationReasonKind.NewAtSource,
            RecommendationReasonKind.TopRated,
        ];

        //Act
        var missing = RecommendationScoring.Score([RecommendationReasonKind.MissingSeasons], recommenders: 0, 0, 0, rating: 7.5, voteCount: 2000);
        var best = others.Max(kind => RecommendationScoring.Score([kind], recommenders: 14, 0, 0, rating: 7.5, voteCount: 2000));

        //Assert
        // Fourteen recommenders, 2·log2(15) ≈ 7.8, are the most still under the missing seasons' 8.
        Assert.True(missing > best, $"The missing seasons scored {missing}, the best other reason {best}.");
    }

    [Fact]
    public void GenreAffinity_is_the_largest_share_of_seeds_holding_one_of_the_genres()
    {
        //Arrange
        IReadOnlyList<IReadOnlyCollection<int>> seeds = [[28, 12], [28], [18], [35]];

        //Act
        var affinity = RecommendationScoring.GenreAffinity([28, 18], seeds);

        //Assert
        // Action is in two of four seeds, Drama in one: the larger share.
        Assert.Equal(0.5, affinity, 9);
    }

    [Fact]
    public void GenreAffinity_without_seeds_or_genres_is_zero()
    {
        //Arrange

        //Act
        var affinities = (RecommendationScoring.GenreAffinity([28], []), RecommendationScoring.GenreAffinity([], [[28]]));

        //Assert
        Assert.Equal((0.0, 0.0), affinities);
    }

    [Fact]
    public void DecadeAffinity_is_the_share_of_seeds_from_the_works_decade_counting_seeds_of_no_year()
    {
        //Arrange
        int?[] seeds = [1986, 1979, 1984, null];

        //Act
        var affinity = RecommendationScoring.DecadeAffinity(1980, seeds);

        //Assert
        Assert.Equal(0.5, affinity, 9);
        Assert.Equal(0.0, RecommendationScoring.DecadeAffinity(null, seeds), 9);
    }

    [Theory]
    [InlineData(1997, 1990)]
    [InlineData(2000, 2000)]
    [InlineData(2009, 2000)]
    public void DecadeOf_is_the_first_year_of_its_decade(int year, int decade)
    {
        //Arrange

        //Act
        var first = RecommendationScoring.DecadeOf(year);

        //Assert
        Assert.Equal(decade, first);
    }
}
