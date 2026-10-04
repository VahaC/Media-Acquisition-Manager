using System.Globalization;
using System.Xml.Linq;
using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.TestInfrastructure;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-32, ADR 0020 — which show a TV release's title claims, read against every title of the real Toloka
// TV answers as Prowlarr rewrote them, Toloka's own titles that Prowlarr rewrote, and the other shapes
// trackers write: the titles, the year or the span of years, and the seasons or the episodes of one season
// that the first season marker claims. A bracketed year or span ends the title as a movie's year does, a
// season marker before it is cut from each title, and without a year the marker itself ends the title. A
// movie's title is read as before. Seasons and years are written "first-last", episodes "season:first-last".
// The years a library series must be of to match a release by title and year follow from its seasons:
// with season 1 its first year exactly, with later seasons only any year up to it, with none its span.
public sealed class ReleaseWorkClaimsTvTests
{
    private static readonly string[] None = [];

    private static readonly string[] Yellowstone = ["Yellowstone"];

    private static readonly string[] MickeyMouseClubhouse = ["Mickey Mouse Clubhouse"];

    /// <summary>Every title of the three TV samples, and what it claims.</summary>
    public static TheoryData<string, string[], string?, string?> SampleTitles() => new()
    {
        { "Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng", Yellowstone, "2018-2024", "1-5" },
        // Prowlarr's repeat of the marker at the end says nothing new.
        { "Yellowstone (S5) (2022-2024) WEB-DL 1080p (S5)", Yellowstone, "2022-2024", "5-5" },
        { "Yellowstone (S5) (2022-2024) WEB-DL 1080p Ukr/Eng | Sub Eng (S5)", Yellowstone, "2022-2024", "5-5" },
        { "Yellowstone (S4) (2021) WEB-DL 1080p Ukr/Eng | Sub Eng (S4)", Yellowstone, "2021", "4-4" },
        { "Yellowstone (S1-3) (2018-2020) WEBRip 1080p Ukr/Eng", Yellowstone, "2018-2020", "1-3" },
        // The BBC documentary of the same title: only its year tells it from the 2018 show.
        { "Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)", Yellowstone, "2009", "1-1" },
        { "World Natural Heritage USA: Yellowstone National Park (2013) IPTVRemux", ["World Natural Heritage USA: Yellowstone National Park"], "2013", null },
        { "Wild Yellowstone She Wolf (2013) HDTV", ["Wild Yellowstone She Wolf"], "2013", null },
        { "Wild Yellowstone She Wolf (2013) HDTV 1080i Ukr/Eng", ["Wild Yellowstone She Wolf"], "2013", null },
        {
            "Mickey Mouse Clubhouse (S1-4) (2006-2016) WEB-DL 1080p/DVDRip-AVC Ukr/Eng | sub Ukr (S1-4)",
            MickeyMouseClubhouse, "2006-2016", "1-4"
        },
        { "Mickey Mouse Clubhouse (S2) (2008-2010) SatRip (S2)", MickeyMouseClubhouse, "2008-2010", "2-2" },
        { "Mickey Mouse Clubhouse (S1) (2006-2007) Satrip (S1)", MickeyMouseClubhouse, "2006-2007", "1-1" },
    };

    /// <summary>Toloka's own titles, as the samples' descriptions keep them, and the other shapes.</summary>
    public static TheoryData<string, string[], string?, string?> Titles() => new()
    {
        // Toloka's "localized (seasons) / original (seasons) (span)": each alternative without its marker.
        { "Єллоустоун (Сезони 1-5) / Yellowstone (Seasons 1-5) (2018-2024) WEB-DLRip-AVC Ukr/Eng | Sub Eng", ["Єллоустоун", "Yellowstone"], "2018-2024", "1-5" },
        { "Єллоустоун (Сезон 1)  / Yellowstone (Season 1) (2009)  BDRip 1080p H.265", ["Єллоустоун", "Yellowstone"], "2009", "1-1" },
        { "Клуб Міккі Мауса (Сезон 1-4) / Mickey Mouse Clubhouse (Season 1-4) (2006-2016) WEB-DL", ["Клуб Міккі Мауса", "Mickey Mouse Clubhouse"], "2006-2016", "1-4" },
        { "Шоу (Сезони 1-3) (2018-2020)", ["Шоу"], "2018-2020", "1-3" },
        { "Шоу (сезон 2, серія 5) (2019)", ["Шоу"], "2019", "2:5-5" },
        { "Шоу (Сезон 1, серії 1-9 з 9) / Show (Season 1, episodes 1-9 of 9) (2018)", ["Шоу", "Show"], "2018", "1:1-9" },
        // Prowlarr's rewrite of "Сезон 1, серії 1-9 з 9".
        { "Show (S1E1-9 of 9) (2018) WEB-DL", ["Show"], "2018", "1:1-9" },
        { "Show (Season 1) (2018)", ["Show"], "2018", "1-1" },
        { "Show (Seasons 1-2) (2018-2019)", ["Show"], "2018-2019", "1-2" },
        { "Show season 1-4 (2010-2014)", ["Show"], "2010-2014", "1-4" },
        { "Show [S3] (2020) WEB-DL", ["Show"], "2020", "3-3" },
        { "Show S01E01-E10 (2018)", ["Show"], "2018", "1:1-10" },
        // A span alone, in either bracket and with a dash; the same year twice is one year.
        { "Show (2010-2012) WEB-DL", ["Show"], "2010-2012", null },
        { "Show [2010-2012]", ["Show"], "2010-2012", null },
        { "Show (2010–2012)", ["Show"], "2010-2012", null },
        { "Show (2018-2018) WEB-DL", ["Show"], "2018", null },
        // A scene name: its marker or its bare year ends the title, whichever comes first.
        { "Yellowstone.S01E01.1080p.WEB-DL", Yellowstone, null, "1:1-1" },
        { "Yellowstone.s02e03.720p", Yellowstone, null, "2:3-3" },
        { "The.Walking.Dead.S01E01-E06.720p", ["The Walking Dead"], null, "1:1-6" },
        { "Show S01E01-10 1080p", ["Show"], null, "1:1-10" },
        { "Show S01E01E02 1080p", ["Show"], null, "1:1-2" },
        { "Show S01E01-S01E10 1080p", ["Show"], null, "1:1-10" },
        { "Show S1-3 1080p", ["Show"], null, "1-3" },
        { "The.Show.2018.S01E01.1080p", ["The Show"], "2018", "1:1-1" },
        // A show named by a year keeps it.
        { "1883.S01E01.1080p", ["1883"], null, "1:1-1" },
        { "1883 (S1) (2021-2022) WEB-DL", ["1883"], "2021-2022", "1-1" },
        // The short forms are written without spaces: a resolution after a dash is no season.
        { "Show S1 - 2160p", ["Show"], null, "1-1" },
        // A range that runs backwards claims its first season alone; episodes of two seasons, neither's.
        { "Show (S5-3) (2020)", ["Show"], "2020", "5-5" },
        { "Show (Сезони 1-2, серії 1-5) (2020)", ["Show"], "2020", "1-2" },
        { "Show S01E05-S02E03 1080p", ["Show"], null, "1-2" },
        // The specials.
        { "Show (S0) (2020)", ["Show"], "2020", "0-0" },
        // A whole word only: "Season" without a number, a letter before the "S".
        { "Show Season of Love (2020) WEB-DL", ["Show Season of Love"], "2020", null },
        { "PS1 Classics (2020)", ["PS1 Classics"], "2020", null },
        // A marker that opens the title: no title, but still the seasons.
        { "S01E01 1080p WEB-DL", None, null, "1:1-1" },
        // No year and no marker: no title, as a movie's.
        { "Show BDRip 1080p", None, null, null },
        // A span that runs backwards is no year at all.
        { "Show (2012-2010) WEB-DL", None, null, null },
        { "", None, null, null },
    };

    [Theory]
    [MemberData(nameof(SampleTitles))]
    public void ReadTv_claims_what_each_title_of_the_real_answers_names(string title, string[] expectedTitles, string? years, string? episodes)
    {
        //Arrange

        //Act
        var claim = ReleaseWorkClaims.ReadTv(title);

        //Assert
        Assert.Equal(expectedTitles, claim.Titles);
        Assert.Equal(years, Years(claim));
        Assert.Equal(episodes, Episodes(claim));
    }

    [Theory]
    [MemberData(nameof(Titles))]
    public void ReadTv_claims_the_titles_the_years_and_the_seasons_the_title_names(string title, string[] expectedTitles, string? years, string? episodes)
    {
        //Arrange

        //Act
        var claim = ReleaseWorkClaims.ReadTv(title);

        //Assert
        Assert.Equal(expectedTitles, claim.Titles);
        Assert.Equal(years, Years(claim));
        Assert.Equal(episodes, Episodes(claim));
    }

    [Theory]
    [InlineData("prowlarr-toloka-tv-search.xml")]
    [InlineData("prowlarr-toloka-tv-season-search.xml")]
    [InlineData("prowlarr-toloka-tv-cartoon-search.xml")]
    public void SampleTitles_cover_every_title_of_the_real_answers(string sample)
    {
        //Arrange
        var document = XDocument.Load(Path.Combine(RepositoryPaths.VendorSamples, "Torznab", sample));

        //Act
        var titles = document.Descendants("item").Select(item => (string)item.Element("title")!).ToHashSet(StringComparer.Ordinal);

        //Assert
        // Guards the table above: a refreshed sample with a new title fails here until the title is read.
        Assert.NotEmpty(titles);
        Assert.Subset(SampleTitles().Select(row => row.Data.Item1).ToHashSet(StringComparer.Ordinal), titles);
    }

    [Theory]
    // A movie's range is a collection's, and a season marker is part of its title: read as before MAM-32.
    [InlineData("Mickey Mouse Clubhouse (S1-4) (2006-2016) WEB-DL 1080p", new string[0], null)]
    [InlineData("Yellowstone (S4) (2021) WEB-DL 1080p", new[] { "Yellowstone (S4)" }, 2021)]
    [InlineData("Yellowstone.S01E01.1080p.WEB-DL", new string[0], null)]
    public void Read_of_a_movie_reads_no_span_and_no_season(string title, string[] expectedTitles, int? expectedYear)
    {
        //Arrange

        //Act
        var claim = ReleaseWorkClaims.Read(title);

        //Assert
        Assert.Equal(expectedTitles, claim.Titles);
        Assert.Equal(expectedYear, claim.Year);
        Assert.Null(claim.LastYear);
        Assert.Null(claim.Episodes);
    }

    [Theory]
    // Season 1 among the seasons: the first year is the show's, exactly, whatever the span.
    [InlineData("Yellowstone (S1-5) (2018-2024) WEB-DL-AVC Ukr/Eng | Sub Eng", "2018")]
    [InlineData("Yellowstone (S1) (2009) BDRip 1080p H.265 Ukr/Eng | Sub Eng (S1)", "2009")]
    [InlineData("Mickey Mouse Clubhouse (S1) (2006-2007) Satrip (S1)", "2006")]
    [InlineData("Show (S0-2) (2017-2019) WEB-DL", "2017")]
    [InlineData("Show (S1E1-9 of 9) (2018) WEB-DL", "2018")]
    [InlineData("The.Show.2018.S01E01.1080p", "2018")]
    // Later seasons only, or the specials alone: any year up to the first, written "..first".
    [InlineData("Yellowstone (S5) (2022-2024) WEB-DL 1080p (S5)", "..2022")]
    [InlineData("Yellowstone (S4) (2021) WEB-DL 1080p Ukr/Eng | Sub Eng (S4)", "..2021")]
    [InlineData("Mickey Mouse Clubhouse (S2) (2008-2010) SatRip (S2)", "..2008")]
    [InlineData("Шоу (сезон 2, серія 5) (2019)", "..2019")]
    [InlineData("Show (S0) (2020)", "..2020")]
    // No season: the span, or the one year, as before.
    [InlineData("Show (2010-2012) WEB-DL", "2010-2012")]
    [InlineData("Wild Yellowstone She Wolf (2013) HDTV", "2013")]
    // No year: none to match.
    [InlineData("Yellowstone.S01E01.1080p.WEB-DL", null)]
    [InlineData("Show BDRip 1080p", null)]
    public void YearsToMatch_of_a_tv_title_follows_the_seasons_it_claims(string title, string? expected)
    {
        //Arrange
        var claim = ReleaseWorkClaims.ReadTv(title);

        //Act
        var years = ReleaseWorkClaims.YearsToMatch(claim);

        //Assert
        Assert.Equal(expected, Describe(years));
    }

    [Theory]
    [InlineData("Interstellar (2014) BDRemux 1080p", "2014")]
    [InlineData("Interstellar.2014.1080p.BluRay", "2014")]
    [InlineData("Interstellar BDRip 1080p", null)]
    public void YearsToMatch_of_a_movie_title_is_its_one_year(string title, string? expected)
    {
        //Arrange
        var claim = ReleaseWorkClaims.Read(title);

        //Act
        var years = ReleaseWorkClaims.YearsToMatch(claim);

        //Assert
        Assert.Equal(expected, Describe(years));
    }

    private static string? Describe(ClaimedYears? years) =>
        years switch
        {
            null => null,
            { Earliest: null } => string.Create(CultureInfo.InvariantCulture, $"..{years.Latest}"),
            { Earliest: { } earliest } when earliest == years.Latest => earliest.ToString(CultureInfo.InvariantCulture),
            { Earliest: { } earliest } => string.Create(CultureInfo.InvariantCulture, $"{earliest}-{years.Latest}"),
        };

    private static string? Years(ReleaseWorkClaim claim) =>
        claim.Year is not { } year
            ? null
            : claim.LastYear is { } lastYear
                ? string.Create(CultureInfo.InvariantCulture, $"{year}-{lastYear}")
                : year.ToString(CultureInfo.InvariantCulture);

    private static string? Episodes(ReleaseWorkClaim claim) =>
        claim.Episodes switch
        {
            null => null,
            { FirstEpisode: { } first } episodes => string.Create(CultureInfo.InvariantCulture, $"{episodes.FirstSeason}:{first}-{episodes.LastEpisode}"),
            var episodes => string.Create(CultureInfo.InvariantCulture, $"{episodes.FirstSeason}-{episodes.LastSeason}"),
        };
}
