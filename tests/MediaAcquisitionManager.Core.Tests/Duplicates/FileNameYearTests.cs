using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-37 — the year a file name claims in brackets (Vasyl, 2026-10-08: copies whose names claim years more than one apart
// are not copies of each other — Blade Runner 2049 under the 1982 film's ids). Read narrowly, as a year read by mistake
// can only take a real copy off the Duplicates page: alone in round or square brackets, never beside a word or a number
// in them, 1880 to 2099, one year per name. The names are production's, the file names of its same-work groups of
// 2026-10-08, unless a comment says otherwise.
public sealed class FileNameYearTests
{
    [Theory]
    [InlineData("[01] Blade Runner (1982) [The Final Cut] UHD-BDRip 1080p H.265 SDR [2xUKR_ENG] [Hurtom].mkv", 1982)]
    // The year in brackets is the claim; the 2049 before it is the title's.
    [InlineData("[02] Blade Runner 2049 (2017) [US Transfer] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2017)]
    [InlineData("Seksmisja (1984).mp4", 1984)]
    [InlineData("Nóż w wodzie (1962).mkv", 1962)]
    [InlineData("99-1. The Egg and Jerry (1956).mkv", 1956)]
    [InlineData("99-2. The Egg and Jerry (1956) (Widescreen).mkv", 1956)]
    [InlineData("105-1. Tops with Pops (1957).mkv", 1957)]
    [InlineData("105-2. Tops with Pops (1957) (Widescreen).mkv", 1957)]
    [InlineData("107-1. Feedin' the Kiddie (1957).mkv", 1957)]
    [InlineData("107-2. Feedin' the Kiddie (1957) (Widescreen).mkv", 1957)]
    [InlineData("[01.1] The Bourne Identity (2002) [Theatrical Edition] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2002)]
    [InlineData("[01.2] The Bourne Identity (2002) [Limited Edition, Open Matte] HybridRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv", 2002)]
    [InlineData("The Gods Must Be Crazy (1980) [2xUkr,Eng sub.Eng] WEB-DL 1080p [Hurtom].mkv", 1980)]
    [InlineData("The Gods Must Be Crazy (1980) WEB-DLRip-AVC.mkv", 1980)]
    [InlineData("[05.1] Rambo. Last Blood (2019) [Theatrical Cut, US Transfer] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2019)]
    [InlineData("[05.2] Rambo. Last Blood (2019) [Extended Cut, FRA Transfer] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2019)]
    [InlineData("[02.1] Terminator 2. Judgment Day (1991) [UK Remastered, Theatrical Cut] BDRip 1080p H.265 [4xUKR_ENG] [Hurtom].mkv", 1991)]
    [InlineData("[02.2] Terminator 2. Judgment Day (1991) [Lionsgate Transfer, Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv", 1991)]
    [InlineData("[02.3] Terminator 2. Judgment Day (1991) [JPN Transfer, Special Extended Edition] BDRip 1080p H.265 [4xUKR_ENG] [Hurtom].mkv", 1991)]
    [InlineData("[04.1] Terminator Salvation (2009) [Theatrical Cut] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv", 2009)]
    [InlineData("[04.2] Terminator Salvation (2009) [Director's Cut] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2009)]
    [InlineData("[01.1] Alien (1979) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 1979)]
    [InlineData("[01.2] Alien (1979) [Director's Cut] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv", 1979)]
    [InlineData("[04.1] Alien. Resurrection (1997) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 1997)]
    [InlineData("[04.2] Alien. Resurrection (1997) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv", 1997)]
    [InlineData("Alien Romulus (2024) BDRemux 2160p 2xUkr Eng [Hurtom].mkv", 2024)]
    [InlineData("[07] Alien. Romulus (2024) BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv", 2024)]
    [InlineData("[03.1] Alien³ (1992) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv", 1992)]
    [InlineData("[03.2] Alien³ (1992) [Special Assembly Cut] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv", 1992)]
    [InlineData("[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv", 1986)]
    [InlineData("[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv", 1986)]
    [InlineData("[02.1] Aliens vs Predator. Requiem (2007) [Theatrical Cut] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2007)]
    [InlineData("[02.2] Aliens vs Predator. Requiem (2007) [Unrated Cut] BDRip 1080p H.265 [UKR_ENG] [Hurtom].mkv", 2007)]
    public void Read_of_a_production_name_is_the_year_in_its_brackets(string fileName, int year)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Equal(year, read);
    }

    [Theory]
    [InlineData("Nóż w wodzie [1962].mkv", 1962)]
    [InlineData("Nóż w wodzie ( 1962 ).mkv", 1962)]
    [InlineData("Nóż w wodzie [ 1962 ].mkv", 1962)]
    // One year twice is one claim.
    [InlineData("Seksmisja (1984) [1984].mkv", 1984)]
    // A year beside a word in a bracket claims nothing, so the year alone is the one claim — the remaster's 2019 is no
    // second year.
    [InlineData("Seksmisja (1984) [1983 DVDRemux].mkv", 1984)]
    [InlineData("Taken (2008) [2019 Remaster].mkv", 2008)]
    // The ends of the range.
    [InlineData("Roundhay Garden Scene (1888).mkv", 1888)]
    [InlineData("Old (1880).mkv", 1880)]
    [InlineData("Late (2099).mkv", 2099)]
    public void Read_of_a_year_alone_in_brackets_is_that_year(string fileName, int year)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Equal(year, read);
    }

    [Theory]
    // The featurette filed with «Ніж у воді» and production's episode names: no year anywhere.
    [InlineData("Polanski+Skolimowski o Noże.mkv")]
    [InlineData("Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv")]
    [InlineData("The Penguins Of Madagascar - S01E25 - Roger Dodger WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ].mkv")]
    // Numbers, resolutions and codecs, in brackets or not.
    [InlineData("[02.3] Terminator 2. Judgment Day [JPN Transfer] BDRip 1080p H.265 [4xUKR_ENG] [Hurtom].mkv")]
    [InlineData("107-2. Feedin' the Kiddie (Widescreen).mkv")]
    [InlineData("Alien Romulus [2160p] [H.265].mkv")]
    [InlineData("Alien Romulus (2160p).mkv")]
    [InlineData("Alien Romulus [1920x1080].mkv")]
    [InlineData("Alien Romulus [1920 x 1080].mkv")]
    [InlineData("Alien Romulus [2024p].mkv")]
    public void Read_of_a_name_with_no_year_in_brackets_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    // A bare year is a title's as often as a release's.
    [InlineData("Blade Runner 2049 BDRip 1080p.mkv")]
    [InlineData("Blade.Runner.2049.1080p.mkv")]
    [InlineData("1917 BDRip 1080p.mkv")]
    [InlineData("Interstellar.2014.1080p.BluRay.mkv")]
    public void Read_of_a_bare_year_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    // A list or a span is a collection's, as production's folders "(2004, 2007)" and "(1940-1967)" are.
    [InlineData("The Gods Must Be Crazy. Dilogy (1980, 1989).mkv")]
    [InlineData("Tom & Jerry e001-e161 (1940-1967).mkv")]
    [InlineData("Yellowstone (2018–2024).mkv")]
    [InlineData("Aliens vs Predator [2004, 2007].mkv")]
    // A year beside a word or a number in brackets is a title's, a release's, a remaster's or a cut's as often as the
    // film's: production's Seksmisja remux claims none, nor does the Redux of a 1979 film.
    [InlineData("Seksmisja [1983 DVDRemux].mkv")]
    [InlineData("Odyssey (2001 A Space Odyssey).mkv")]
    [InlineData("Apocalypse Now [2001 Redux].mkv")]
    [InlineData("Star Wars [1997 Special Edition].mkv")]
    [InlineData("Taken [2019 Remaster].mkv")]
    [InlineData("Seksmisja [DVDRemux 1983].mkv")]
    [InlineData("Seksmisja [1983 1080p].mkv")]
    // Brackets that don't match.
    [InlineData("Blade Runner (1982].mkv")]
    public void Read_of_a_list_a_span_or_a_year_among_other_words_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    // Nothing says which is the film's.
    [InlineData("Blade Runner (1982) [2017].mkv")]
    [InlineData("Blade Runner (1982) (2017).mkv")]
    public void Read_of_a_name_claiming_two_different_years_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData("Old (1879).mkv")]
    [InlineData("Late (2100).mkv")]
    // ASCII digits only: Arabic-Indic digits are no year.
    [InlineData("Blade Runner (١٩٨٢).mkv")]
    public void Read_of_a_year_out_of_range_or_of_other_digits_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_of_a_blank_name_is_nothing(string? fileName)
    {
        //Arrange

        //Act
        var read = FileNameYear.Read(fileName);

        //Assert
        Assert.Null(read);
    }
}
