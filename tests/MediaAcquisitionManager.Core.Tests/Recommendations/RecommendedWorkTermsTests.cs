using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Core.Tests.Recommendations;

// MAM-33 — a recommended catalog work as a release is matched against it, the one reading the availability check and
// the list's download jobs share: its titles with its year, its ids in its kind's numbering, a held series' missing
// seasons.
public sealed class RecommendedWorkTermsTests
{
    [Fact]
    public void TitlesOf_lists_the_catalog_title_and_an_original_title_that_differs_each_with_the_year()
    {
        //Arrange

        //Act
        var both = RecommendedWorkTerms.TitlesOf("Інтерстеллар", "Interstellar", 2014);
        var same = RecommendedWorkTerms.TitlesOf("Брат", "Брат", 1997);

        //Assert
        Assert.Equal(
            [new ReportedTitle("Інтерстеллар", MediaTitleKind.Display, 2014), new ReportedTitle("Interstellar", MediaTitleKind.Original, 2014)],
            both);
        Assert.Equal([new ReportedTitle("Брат", MediaTitleKind.Display, 1997)], same);
    }

    [Theory]
    [InlineData(MediaWorkKind.Movie, MediaIdentifierScheme.Tmdb)]
    [InlineData(MediaWorkKind.Series, MediaIdentifierScheme.TmdbTv)]
    public void IdentifiersOf_numbers_the_catalog_id_by_the_works_kind_and_adds_a_known_imdb_id(MediaWorkKind kind, MediaIdentifierScheme scheme)
    {
        //Arrange

        //Act
        var withImdb = RecommendedWorkTerms.IdentifiersOf(kind, "73586", "tt4236770");
        var withoutImdb = RecommendedWorkTerms.IdentifiersOf(kind, "73586", null);

        //Assert
        Assert.Equal([new MediaIdentifier(scheme, "73586"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt4236770")], withImdb);
        Assert.Equal([new MediaIdentifier(scheme, "73586")], withoutImdb);
    }

    [Theory]
    [InlineData("4,5", new[] { 4, 5 })]
    [InlineData(" 4 , x ,5,", new[] { 4, 5 })]
    [InlineData("", new int[0])]
    [InlineData(null, new int[0])]
    public void MissingSeasonsOf_reads_the_runs_list_and_skips_what_is_no_number(string? missingSeasons, int[] expected)
    {
        //Arrange

        //Act
        var seasons = RecommendedWorkTerms.MissingSeasonsOf(missingSeasons);

        //Assert
        Assert.Equal(expected, seasons.Order());
    }
}
