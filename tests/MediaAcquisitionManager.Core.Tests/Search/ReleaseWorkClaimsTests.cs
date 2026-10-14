using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-12 — which work a release's title claims to be, read for matching it to the library by title and
// year: a bracketed year first, then a bare scene-style one, and no title at all without a year.
public sealed class ReleaseWorkClaimsTests
{
    private static readonly string[] None = [];

    /// <summary>Every example the specification pins, and the shapes around them.</summary>
    public static TheoryData<string, string[], int?> Titles() => new()
    {
        // Toloka's own shape: the year in brackets, the quality after it.
        { "Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", ["Interstellar"], 2014 },
        // Toloka's localized title beside the original one.
        { "Інтерстеллар / Interstellar (2014) BDRip", ["Інтерстеллар", "Interstellar"], 2014 },
        // A scene name.
        { "Interstellar.2014.1080p.BluRay.x264-SPARKS", ["Interstellar"], 2014 },
        { "Interstellar_2014_1080p", ["Interstellar"], 2014 },
        // A year inside the title stays in it.
        { "Blade Runner 2049 (2017) 2160p", ["Blade Runner 2049"], 2017 },
        { "1917 (2019) 1080p", ["1917"], 2019 },
        { "2001: A Space Odyssey (1968)", ["2001: A Space Odyssey"], 1968 },
        // A bare title that is a year, and a run of years that ends at the last.
        { "2012.2009.1080p", ["2012"], 2009 },
        { "Blade.Runner.2049.2017.1080p", ["Blade Runner 2049"], 2017 },
        // The first bracketed year wins over a later bare one.
        { "Taken (2008) Remastered 2019", ["Taken"], 2008 },
        // A slash without whitespace on both sides is part of the title.
        { "Face/Off (1997)", ["Face/Off"], 1997 },
        { "Interstellar [2014] 1080p", ["Interstellar"], 2014 },
        // What stands between a title and its year is trimmed off, and a blank alternative dropped.
        { "Interstellar - (2014)", ["Interstellar"], 2014 },
        { "— Interstellar: (2014)", ["Interstellar"], 2014 },
        { "Інтерстеллар / - / Interstellar (2014)", ["Інтерстеллар", "Interstellar"], 2014 },
        // MAM-12 — an apostrophe is part of the title, never an edge to trim: matching drops it later.
        { "Ocean's Eleven (2001) BDRip 1080p", ["Ocean's Eleven"], 2001 },
        { "Ocean's.Eleven.2001.1080p.BluRay", ["Ocean's Eleven"], 2001 },
        { "Oceans.Eleven.2001.1080p.BluRay", ["Oceans Eleven"], 2001 },
        { "Дев\u2019ять / 9 (2009) BDRip", ["Дев\u2019ять", "9"], 2009 },
        { "'Round Midnight' (1986)", ["'Round Midnight'"], 1986 },
        // MAM-30 — a Ukrainian-only title after the gateway stripped its letters (Jackett's default, Prowlarr's too): its
        // quotes are a title whose match key is empty, which matches nothing; the description keeps the whole title.
        { "« » (2026) WEB-DL-AVC Ukr | Sub Ukr/Eng", ["« »"], 2026 },
        { "Потяг «Червона Рута» (2026) WEB-DLRip-AVC Ukr | Sub Ukr/Eng", ["Потяг «Червона Рута»"], 2026 },
        // No year: the title can't be told apart from its quality tail.
        { "Interstellar BDRip 1080p", None, null },
        // A bracketed year with no title before it isn't one, and a bare year needs a word before it.
        { "(2014) Interstellar", None, null },
        // A range is a collection's span, not one work's year.
        { "Some Saga (2010-2014) Collection", None, null },
        // Brackets that don't match, and years no film is from.
        { "Interstellar (2014] 1080p", None, null },
        { "Interstellar (1799)", None, null },
        { "Interstellar.2100.1080p", None, null },
        { "", None, null },
    };

    [Theory]
    [MemberData(nameof(Titles))]
    public void Read_claims_the_titles_and_the_year_the_title_names(string title, string[] expectedTitles, int? expectedYear)
    {
        //Arrange

        //Act
        var claim = ReleaseWorkClaims.Read(title);

        //Assert
        Assert.Equal(expectedTitles, claim.Titles);
        Assert.Equal(expectedYear, claim.Year);
    }

    /// <summary>MAM-12 review, Vasyl's decision 4 — the "rambo" releases Toloka listed, and the shapes
    /// around them.</summary>
    public static TheoryData<string, string[], int?> TitlesWithSquareBrackets() => new()
    {
        // An edition tag between the title and its year is not part of the title.
        { "Rambo: Last Blood [Extended Cut] (2019) BDRip-AVC", ["Rambo: Last Blood"], 2019 },
        // It ends an alternative as it ends the title.
        { "V / Rambo [Theatrical Cut] (2008) BDRip-AVC 2xUkr/Eng", ["V", "Rambo"], 2008 },
        { "Rambo [Theatrical Cut] / Рембо IV (2008)", ["Rambo", "Рембо IV"], 2008 },
        // Every tag that ends it, with what stands between them.
        { "Rambo: Last Blood [Extended Cut] - [Remastered] (2019)", ["Rambo: Last Blood"], 2019 },
        // A scene name's title the same.
        { "Rambo.Last.Blood.[Extended.Cut].2019.1080p", ["Rambo Last Blood"], 2019 },
        // A title that opens with or holds square brackets keeps them, and so does one that is nothing else.
        { "[REC] 2 (2009) BDRip", ["[REC] 2"], 2009 },
        { "[REC] (2007) BDRip", ["[REC]"], 2007 },
        { "[REC] [Extended Cut] (2007)", ["[REC]"], 2007 },
        // Round brackets are never a tag: they hold titles.
        { "Birdman or (The Unexpected Virtue of Ignorance) (2014)", ["Birdman or (The Unexpected Virtue of Ignorance)"], 2014 },
    };

    [Theory]
    [MemberData(nameof(TitlesWithSquareBrackets))]
    public void Read_drops_only_the_square_bracketed_tags_that_end_a_title(string title, string[] expectedTitles, int? expectedYear)
    {
        //Arrange

        //Act
        var claim = ReleaseWorkClaims.Read(title);

        //Assert
        Assert.Equal(expectedTitles, claim.Titles);
        Assert.Equal(expectedYear, claim.Year);
    }

    [Fact]
    public void Read_takes_the_first_bracketed_year_with_a_title_before_it()
    {
        //Arrange
        // "[2014]" opens the title and says nothing about which title it is; "(2014)" ends it.
        const string title = "[2014] Interstellar (2014) BDRip";

        //Act
        var claim = ReleaseWorkClaims.Read(title);

        //Assert
        Assert.Equal(["[2014] Interstellar"], claim.Titles);
        Assert.Equal(2014, claim.Year);
    }
}
