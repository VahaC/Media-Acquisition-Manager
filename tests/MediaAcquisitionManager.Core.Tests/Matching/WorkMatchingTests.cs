using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Matching;

// MAM-12 — whether a library work is the one a search or a release claims, and on what evidence
// (specification §3.1's priorities): an identifier, then the title with the year, then the title alone.
// Every conflict is flagged and none resolved, and nothing weaker than an exact key or year matches. Below
// the matches, a work one of whose titles or file paths holds every word of a title sought is listed by
// its words — whole words, in one text — never as a match (ADR 0012).
public sealed class WorkMatchingTests
{
    private static readonly ReportedTitle InterstellarDisplay = new("Інтерстеллар", MediaTitleKind.Display, 2014);
    private static readonly ReportedTitle InterstellarOriginal = new("Interstellar", MediaTitleKind.Original, 2014);
    private static readonly ReportedTitle[] Interstellar = [InterstellarDisplay, InterstellarOriginal];
    private static readonly MediaIdentifier[] InterstellarIds = [Imdb("tt0816692"), Tmdb("157336")];

    /// <summary>"Рембо. Перша кров. Частина друга" (1985), originally "Rambo: First Blood Part II", which
    /// another source displays as "Rambo II".</summary>
    private static readonly ReportedTitle[] RamboFirstBloodPartII =
    [
        new("Rambo: First Blood Part II", MediaTitleKind.Original, 1985),
        new("Rambo II", MediaTitleKind.Display, 1985),
        new("Рембо. Перша кров. Частина друга", MediaTitleKind.Display, 1985),
    ];

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
        var claim = new WorkClaim(["interstellar"], ClaimedYears.Exactly(2014), [Imdb("tt0816692")]);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(2015), [Tmdb("157336")]);

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
        var claim = new WorkClaim(["Інтерстеллар"], ClaimedYears.Exactly(2014), []);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(2014), []);

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
        var claim = new WorkClaim(["Інтерстеллар", "Interstellar"], ClaimedYears.Exactly(2014), []);

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
        var claim = new WorkClaim(["spider man no way home"], ClaimedYears.Exactly(2021), []);

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
        var claim = new WorkClaim([sought], ClaimedYears.Exactly(year), []);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(2014), []);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(year), []);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, InterstellarIds);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleOnly, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Equal(InterstellarOriginal, match.MatchedTitle);
    }

    [Theory]
    // MAM-32 — a span: the show begun in 2018 is of each year of it, its ends included.
    [InlineData(2018, 2024, MatchEvidence.TitleAndYear)]
    [InlineData(2016, 2018, MatchEvidence.TitleAndYear)]
    [InlineData(2010, 2030, MatchEvidence.TitleAndYear)]
    // A span the show's year is outside: the title alone, never a year either side of it.
    [InlineData(2019, 2024, MatchEvidence.TitleOnly)]
    [InlineData(2009, 2017, MatchEvidence.TitleOnly)]
    // No earliest year, a pack of later seasons': any year up to the latest, the latest included.
    [InlineData(null, 2018, MatchEvidence.TitleAndYear)]
    [InlineData(null, 2022, MatchEvidence.TitleAndYear)]
    [InlineData(null, 2017, MatchEvidence.TitleOnly)]
    // One year: exactly it, as before.
    [InlineData(2018, 2018, MatchEvidence.TitleAndYear)]
    [InlineData(2021, 2021, MatchEvidence.TitleOnly)]
    [InlineData(2017, 2017, MatchEvidence.TitleOnly)]
    public void Match_by_the_title_takes_a_year_within_the_claimed_years(int? earliest, int latest, MatchEvidence expected)
    {
        //Arrange
        ReportedTitle[] yellowstone = [new("Єллоустоун", MediaTitleKind.Display, 2018), new("Yellowstone", MediaTitleKind.Original, 2018)];
        var claim = new WorkClaim(["Yellowstone"], new ClaimedYears(earliest, latest), []);

        //Act
        var match = WorkMatching.Match(claim, yellowstone, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(expected, match.Evidence);
        Assert.Equal(yellowstone[1], match.MatchedTitle);
    }

    [Theory]
    // MAM-32 — the years a TV release's title allows (ReleaseWorkClaims.YearsToMatch), against the show
    // begun in 2018. With season 1, its first year exactly: inside the span is not enough.
    [InlineData("Yellowstone (S1-5) (2018-2024) WEB-DL", MatchEvidence.TitleAndYear)]
    [InlineData("Yellowstone (S1-5) (2017-2024) WEB-DL", MatchEvidence.TitleOnly)]
    [InlineData("Yellowstone (S1) (2019) WEB-DL", MatchEvidence.TitleOnly)]
    // The BBC documentary of 2009.
    [InlineData("Yellowstone (S1) (2009) BDRip", MatchEvidence.TitleOnly)]
    // Later seasons only: the show began that year or before, its own year included.
    [InlineData("Yellowstone (S5) (2022-2024) WEB-DL", MatchEvidence.TitleAndYear)]
    [InlineData("Yellowstone (S4) (2021) WEB-DL", MatchEvidence.TitleAndYear)]
    [InlineData("Yellowstone (S2) (2018) WEB-DL", MatchEvidence.TitleAndYear)]
    // A pack whose first year is before the show began is another show's.
    [InlineData("Yellowstone (S2) (2017) WEB-DL", MatchEvidence.TitleOnly)]
    [InlineData("Yellowstone (S0) (2018) WEB-DL", MatchEvidence.TitleAndYear)]
    [InlineData("Yellowstone (S0) (2017) WEB-DL", MatchEvidence.TitleOnly)]
    // No season: any year of the span, its ends included.
    [InlineData("Yellowstone (2016-2018) WEB-DL", MatchEvidence.TitleAndYear)]
    [InlineData("Yellowstone (2018-2024) WEB-DL", MatchEvidence.TitleAndYear)]
    [InlineData("Yellowstone (2019-2024) WEB-DL", MatchEvidence.TitleOnly)]
    [InlineData("Yellowstone (2018) WEB-DL", MatchEvidence.TitleAndYear)]
    public void Match_of_a_tv_releases_title_takes_the_shows_year_its_seasons_allow(string title, MatchEvidence expected)
    {
        //Arrange
        ReportedTitle[] yellowstone = [new("Yellowstone", MediaTitleKind.Original, 2018)];
        var read = ReleaseWorkClaims.ReadTv(title);
        var claim = new WorkClaim(read.Titles, ReleaseWorkClaims.YearsToMatch(read), []);

        //Act
        var match = WorkMatching.Match(claim, yellowstone, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(expected, match.Evidence);
    }

    [Fact]
    public void Match_with_no_earliest_year_never_takes_a_title_of_no_year()
    {
        //Arrange
        // MAM-32 — "any year up to 2022" says nothing of a title the source gives no year.
        ReportedTitle[] titles = [new("Yellowstone", MediaTitleKind.Original, null)];
        var claim = new WorkClaim(["Yellowstone"], new ClaimedYears(null, 2022), []);

        //Act
        var match = WorkMatching.Match(claim, titles, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleOnly, match.Evidence);
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
        var claim = new WorkClaim([title], ClaimedYears.Exactly(2014), []);

        //Act
        var match = WorkMatching.Match(claim, titles, InterstellarIds);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void Match_of_nothing_in_common_is_null()
    {
        //Arrange
        var claim = new WorkClaim(["Aliens"], ClaimedYears.Exactly(1986), [Imdb("tt0090605"), Tmdb("679")]);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(2014), [Imdb("tt0816692")]);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(2014), [Tmdb("1"), Imdb("tt0000001")]);

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
        var claim = new WorkClaim(["Interstellar"], ClaimedYears.Exactly(2014), [Tmdb("157336")]);

        //Act
        var match = WorkMatching.Match(claim, Interstellar, [Imdb("tt0816692")]);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.TitleAndYear, match.Evidence);
        Assert.Equal(NoConflict, match.ConflictingSchemes);
    }

    [Theory]
    // One word, in whichever title holds it: a displayed one before the original one when both do.
    [InlineData("Rambo", "Rambo II")]
    [InlineData("рембо", "Рембо. Перша кров. Частина друга")]
    // Every word, in any order, letter case and punctuation.
    [InlineData("blood, RAMBO", "Rambo: First Blood Part II")]
    [InlineData("Перша кров", "Рембо. Перша кров. Частина друга")]
    public void MatchByWords_lists_a_work_whose_title_holds_every_word_sought_as_words_evidence(string sought, string expectedTitle)
    {
        //Arrange
        // MAM-12 — the year sought is another film's: the words don't look at it.
        var claim = new WorkClaim([sought], ClaimedYears.Exactly(2008), []);

        //Act
        var match = WorkMatching.MatchByWords(claim, RamboFirstBloodPartII, [], []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Words, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Equal(expectedTitle, match.MatchedTitle?.Text);
        Assert.Equal(NoConflict, match.ConflictingSchemes);
    }

    [Fact]
    public void MatchByWords_lists_a_work_whose_file_path_alone_holds_every_word_without_a_matched_title()
    {
        //Arrange
        // MAM-12 — no title of "First Blood" holds "Rambo"; the collection folder its file is in does.
        ReportedTitle[] titles = [new("Рембо. Перша кров", MediaTitleKind.Display, 1982), new("First Blood", MediaTitleKind.Original, 1982)];
        string[] paths =
        [
            "/media/movies/Rambo. Collection/[01] Rambo. First Blood (1982) [GER Transfer] BDRip 1080p H.265 [3xUKR_ENG] [Hurtom].mkv",
        ];
        var claim = new WorkClaim(["Rambo"], null, []);

        //Act
        var match = WorkMatching.MatchByWords(claim, titles, paths, []);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Words, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Null(match.MatchedTitle);
    }

    [Theory]
    // MAM-12 — a whole word, never a part of a longer one: "Alien" is not in "Aliens".
    [InlineData("Alien")]
    [InlineData("Чуж")]
    // Every word: "2" is in neither title nor the path.
    [InlineData("Aliens 2")]
    // A title of nothing but punctuation seeks nothing.
    [InlineData("!!!")]
    public void MatchByWords_needs_every_word_whole_not_part_of_a_longer_one(string sought)
    {
        //Arrange
        ReportedTitle[] titles = [new("Чужі", MediaTitleKind.Display, 1986), new("Aliens", MediaTitleKind.Original, 1986)];
        string[] paths = ["/media/movies/Aliens (1986)/Aliens.1986.BDRip.mkv"];
        var claim = new WorkClaim([sought], null, []);

        //Act
        var match = WorkMatching.MatchByWords(claim, titles, paths, []);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void MatchByWords_needs_every_word_in_one_title_or_one_path()
    {
        //Arrange
        // MAM-12 — "Чужі" holds one word and "Aliens" the other: no one text holds them both.
        ReportedTitle[] titles = [new("Чужі", MediaTitleKind.Display, 1986), new("Aliens", MediaTitleKind.Original, 1986)];
        string[] paths = ["/media/movies/Aliens (1986).mkv", "/media/movies/Чужі (1986).mkv"];
        var claim = new WorkClaim(["Чужі Aliens"], null, []);

        //Act
        var match = WorkMatching.MatchByWords(claim, titles, paths, []);

        //Assert
        Assert.Null(match);
    }

    [Fact]
    public void MatchByWords_flags_conflicting_ids_as_a_match_does()
    {
        //Arrange
        // MAM-12 — the search asked for an IMDb id the work doesn't hold, and two sources gave it two TMDb
        // ids: listed by its words all the same, with both disagreements, and no id matched.
        var claim = new WorkClaim(["Rambo"], null, [Imdb("tt0095956")]);
        ReportedTitle[] titles = [new("Rambo III", MediaTitleKind.Original, 1988)];

        //Act
        var match = WorkMatching.MatchByWords(claim, titles, [], [Imdb("tt9999999"), Tmdb("1370"), Tmdb("1371")]);

        //Assert
        Assert.NotNull(match);
        Assert.Equal(MatchEvidence.Words, match.Evidence);
        Assert.Empty(match.MatchedIdentifiers);
        Assert.Equal([MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb], match.ConflictingSchemes);
    }

    [Fact]
    public void MatchByWords_of_a_work_holding_none_of_the_words_is_null()
    {
        //Arrange
        var claim = new WorkClaim(["Rambo"], null, []);

        //Act
        var match = WorkMatching.MatchByWords(claim, Interstellar, ["/media/movies/Interstellar (2014).mkv"], InterstellarIds);

        //Assert
        Assert.Null(match);
    }

    private static MediaIdentifier Imdb(string value) => new(MediaIdentifierScheme.Imdb, value);

    private static MediaIdentifier Tmdb(string value) => new(MediaIdentifierScheme.Tmdb, value);
}
