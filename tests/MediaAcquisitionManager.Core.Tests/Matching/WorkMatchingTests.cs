using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Tests.Matching;

// MAM-12 — whether a library work is the one a search or a release claims, and on what evidence
// (specification §3.1's priorities): an identifier, then the title with the year, then the title alone.
// Every conflict is flagged and none resolved, and nothing weaker than an exact key or year matches.
public sealed class WorkMatchingTests
{
    private static readonly ReportedTitle InterstellarDisplay = new("Інтерстеллар", MediaTitleKind.Display, 2014);
    private static readonly ReportedTitle InterstellarOriginal = new("Interstellar", MediaTitleKind.Original, 2014);
    private static readonly ReportedTitle[] Interstellar = [InterstellarDisplay, InterstellarOriginal];
    private static readonly MediaIdentifier[] InterstellarIds = [Imdb("tt0816692"), Tmdb("157336")];

    private static readonly MediaIdentifierScheme[] NoConflict = [];
    private static readonly MediaIdentifierScheme[] ImdbConflict = [MediaIdentifierScheme.Imdb];

    [Theory]
    [InlineData(MediaIdentifierScheme.Imdb, "tt0816692")]
    [InlineData(MediaIdentifierScheme.Tmdb, "157336")]
    public void Match_by_an_identifier_alone_is_identifier_evidence(MediaIdentifierScheme scheme, string value)
    {
        //Arrange
        // A release whose title names no year claims no title, so only its listing's id can match.
        var claim = new WorkClaim([], null, [new MediaIdentifier(scheme, value)]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Identifier, match.Evidence);
        Assert.Equal([new MediaIdentifier(scheme, value)], match.MatchedIdentifiers);
        Assert.Null(match.MatchedTitle);
        Assert.Equal(NoConflict, match.ConflictingSchemes);
    }

    [Fact]
    public void Match_compares_identifiers_trimmed_and_in_any_letter_case_and_gives_the_works_own()
    {
        //Arrange
        var claim = new WorkClaim([], null, [Imdb(" TT0816692 ")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Identifier, match.Evidence);
        Assert.Equal([Imdb("tt0816692")], match.MatchedIdentifiers);
    }

    [Fact]
    public void Match_by_an_identifier_names_each_matching_id_of_the_work_once_as_written()
    {
        //Arrange
        // Two sources give the same id, one of them in capitals: one id, not a conflict.
        MediaIdentifier[] identifiers = [Imdb("tt0816692"), Tmdb("157336"), Imdb("tt0816692"), Imdb("TT0816692")];
        var claim = new WorkClaim([], null, [Imdb("tt0816692"), Tmdb("157336")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, identifiers);

        //Assert
        Assert.NotNull(match);
        Assert.Equal([Imdb("tt0816692"), Tmdb("157336"), Imdb("TT0816692")], match.MatchedIdentifiers);
        Assert.Equal(NoConflict, match.ConflictingSchemes);
    }

    [Fact]
    public void Match_by_an_identifier_names_the_matching_title_of_the_same_year_first()
    {
        //Arrange
        // A second source gives the displayed title another year; the original one has the claim's.
        ReportedTitle[] titles = [new("Interstellar", MediaTitleKind.Display, 2015), InterstellarOriginal];
        var claim = new WorkClaim(["interstellar"], 2014, [Imdb("tt0816692")]);

        //Act
        var match = WorkMatching.Match(claim, titles, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Identifier, match.Evidence);
        Assert.Equal(InterstellarOriginal, match.MatchedTitle);
    }

    [Fact]
    public void Match_by_an_identifier_names_a_matching_title_of_another_year_when_none_has_the_claims()
    {
        //Arrange
        var claim = new WorkClaim(["Interstellar"], 2015, [Tmdb("157336")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Identifier, match.Evidence);
        Assert.Equal(InterstellarOriginal, match.MatchedTitle);
    }

    [Fact]
    public void Match_by_the_displayed_title_and_the_year_is_title_and_year_evidence()
    {
        //Arrange
        var claim = new WorkClaim(["Інтерстеллар"], 2014, []);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Equal(InterstellarDisplay, match.MatchedTitle);
        Assert.Equal(NoConflict, match.ConflictingSchemes);
    }

    [Fact]
    public void Match_by_the_original_title_and_the_year_is_title_and_year_evidence()
    {
        //Arrange
        var claim = new WorkClaim(["Interstellar"], 2014, []);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Equal(InterstellarOriginal, match.MatchedTitle);
    }

    [Fact]
    public void Match_of_a_release_listed_under_both_titles_names_the_displayed_one()
    {
        //Arrange
        // Toloka's "Інтерстеллар / Interstellar (2014)".
        var claim = new WorkClaim(["Інтерстеллар", "Interstellar"], 2014, []);

        //Act
        var match = WorkMatching.Match(claim, [InterstellarOriginal, InterstellarDisplay], InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Equal(InterstellarDisplay, match.MatchedTitle);
    }

    [Fact]
    public void Match_ignores_letter_case_and_punctuation_in_titles()
    {
        //Arrange
        ReportedTitle[] titles = [new("Spider-Man: No Way Home", MediaTitleKind.Display, 2021)];
        var claim = new WorkClaim(["spider man no way home"], 2021, []);

        //Act
        var match = WorkMatching.Match(claim, titles, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Equal(titles[0], match.MatchedTitle);
    }

    [Theory]
    // MAM-12 — the library holds the modifier letter Ukrainian spelling recommends; the search was typed
    // with the typewriter apostrophe.
    [InlineData("Дев\u02BCять", "Дев\u0027ять", 2009)]
    // A scene name leaves the apostrophe out.
    [InlineData("Ocean's Eleven", "Oceans Eleven", 2001)]
    public void Match_of_a_title_written_with_another_apostrophe_or_none_is_title_and_year_evidence(string held, string sought, int year)
    {
        //Arrange
        ReportedTitle[] titles = [new(held, MediaTitleKind.Display, year)];
        var claim = new WorkClaim([sought], year, []);

        //Act
        var match = WorkMatching.Match(claim, titles, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Equal(titles[0], match.MatchedTitle);
    }

    [Fact]
    public void Match_takes_the_year_of_the_title_that_matched_only()
    {
        //Arrange
        // The displayed title has no year; another title's 2014 says nothing about it.
        ReportedTitle[] titles = [new("Interstellar", MediaTitleKind.Display, null), new("Інтерстеллар", MediaTitleKind.Original, 2014)];
        var claim = new WorkClaim(["Interstellar"], 2014, []);

        //Act
        var match = WorkMatching.Match(claim, titles, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleOnly, match.Evidence);
        Assert.Equal(titles[0], match.MatchedTitle);
    }

    [Theory]
    // No year asked.
    [InlineData(null)]
    // Another year — never a year either side of it.
    [InlineData(2015)]
    [InlineData(2013)]
    public void Match_by_the_title_without_its_year_is_title_only_evidence(int? year)
    {
        //Arrange
        var claim = new WorkClaim(["Interstellar"], year, []);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleOnly, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Equal(InterstellarOriginal, match.MatchedTitle);
    }

    [Fact]
    public void Match_by_the_title_alone_names_a_displayed_title_then_an_original_then_the_first()
    {
        //Arrange
        ReportedTitle[] titles =
        [
            new("Aliens", MediaTitleKind.Original, 1986),
            new("ALIENS", MediaTitleKind.Display, 1986),
            new("Aliens", MediaTitleKind.Display, 1986),
        ];
        var claim = new WorkClaim(["aliens"], null, []);

        //Act
        var match = WorkMatching.Match(claim, titles, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(titles[1], match.MatchedTitle);
    }

    [Theory]
    // No other spelling, no shorter or longer title: a weaker look-alike is not a match.
    [InlineData("Interstelar")]
    [InlineData("Interstellar 2")]
    [InlineData("Inter stellar")]
    // A title of nothing but punctuation names no title, even against another such title.
    [InlineData("???")]
    public void Match_of_a_title_whose_key_differs_is_null(string title)
    {
        //Arrange
        ReportedTitle[] titles = [.. Interstellar, new("!!!", MediaTitleKind.Display, 2014)];
        var claim = new WorkClaim([title], 2014, []);

        //Act
        var match = WorkMatching.Match(claim, titles, InterstellarIds);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void Match_of_nothing_in_common_is_null()
    {
        //Arrange
        var claim = new WorkClaim(["Aliens"], 1986, [Imdb("tt0090605"), Tmdb("679")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void Match_of_a_blank_identifier_is_null()
    {
        //Arrange
        // A blank value names nothing, so two of them name no common work.
        var claim = new WorkClaim([], null, [Imdb("  ")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Imdb("")]);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void Match_of_a_conflicting_identifier_falls_to_the_title_and_flags_the_conflict()
    {
        //Arrange
        // The search asked for tt0816692; the work holds another IMDb id.
        var claim = new WorkClaim(["Interstellar"], 2014, [Imdb("tt0816692")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Imdb("tt9999999"), Tmdb("157336")]);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Equal(ImdbConflict, match.ConflictingSchemes);
    }

    [Fact]
    public void Match_of_a_conflicting_identifier_alone_is_null()
    {
        //Arrange
        // A conflict is something to show about a match, never a match of its own.
        var claim = new WorkClaim([], null, [Imdb("tt0816692")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Imdb("tt9999999")]);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void Match_of_a_work_two_sources_give_two_ids_matches_either_and_flags_the_conflict()
    {
        //Arrange
        // ADR 0006: two sources disagree; neither value wins and nothing is merged.
        MediaIdentifier[] identifiers = [Imdb("tt0816692"), Imdb("tt9999999")];
        var claim = new WorkClaim([], null, [Imdb("tt9999999")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, identifiers);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Identifier, match.Evidence);
        Assert.Equal([Imdb("tt9999999")], match.MatchedIdentifiers);
        Assert.Equal(ImdbConflict, match.ConflictingSchemes);
    }

    [Fact]
    public void Match_by_the_title_flags_the_works_own_conflict_too()
    {
        //Arrange
        var claim = new WorkClaim(["Interstellar"], null, []);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Tmdb("157336"), Tmdb("157337"), Imdb("tt0816692")]);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleOnly, match.Evidence);
        Assert.Equal([MediaIdentifierScheme.Tmdb], match.ConflictingSchemes);
    }

    [Fact]
    public void Match_flags_each_conflicting_scheme_once_in_scheme_order()
    {
        //Arrange
        var claim = new WorkClaim(["Interstellar"], 2014, [Tmdb("1"), Imdb("tt0000001")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Tmdb("157336"), Tmdb("157337"), Imdb("tt0816692")]);

        //Assert
        Assert.NotNull(match);
        Assert.Equal([MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb], match.ConflictingSchemes);
    }

    [Fact]
    public void Match_sees_no_conflict_in_a_scheme_only_one_side_holds()
    {
        //Arrange
        // The claim has no IMDb id and the work no TMDb id: nothing to disagree about.
        var claim = new WorkClaim(["Interstellar"], 2014, [Tmdb("157336")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Imdb("tt0816692")]);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Equal(NoConflict, match.ConflictingSchemes);
    }

    private static MediaIdentifier Imdb(string value) => new(MediaIdentifierScheme.Imdb, value);

    private static MediaIdentifier Tmdb(string value) => new(MediaIdentifierScheme.Tmdb, value);
}
