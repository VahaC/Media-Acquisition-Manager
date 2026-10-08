using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-37 — the segment letter a file name glues to its first SxxEyy (Vasyl, 2026-10-08: an episode's "a" and "b" files
// are not copies of each other). Read narrowly, as a letter read by mistake can only keep two copies apart: a lowercase a
// to d standing on its own right after the first compact marker, never a range, a pair, a capital or a word. The Mickey
// Mouse Clubhouse, Penguins of Madagascar and film names are production's.
public sealed class EpisodeSegmentsTests
{
    [Theory]
    [InlineData("Mickey Mouse Clubhouse_S03E09a_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv", "a")]
    [InlineData("Mickey Mouse Clubhouse_S03E09b_-_Road Rally [ukr_eng] WEB-DL 1080p [Hurtom].mkv", "b")]
    [InlineData("Mickey Mouse Clubhouse_S04E26b_-_A Goofy Fairy Tale [ukr_eng] WEB-DL 1080p [Hurtom].mkv", "b")]
    [InlineData("s04e04b.mkv", "b")]
    [InlineData("S03E009a.mkv", "a")]
    [InlineData("S00E01d.mkv", "d")]
    [InlineData("Show_S03E09a_1080p.mkv", "a")]
    // A joiner and a word after the letter, not another segment: "daisy" is no "d".
    [InlineData("Show_S01E02a_daisy.mkv", "a")]
    // The first marker decides; a later one says nothing.
    [InlineData("Show S03E09a (was S03E10b).mkv", "a")]
    public void Read_of_a_letter_glued_to_a_files_first_marker_is_that_letter(string fileName, string segment)
    {
        //Arrange

        //Act
        var read = EpisodeSegments.Read(fileName);

        //Assert
        Assert.Equal(segment, read);
    }

    [Theory]
    [InlineData("Mickey Mouse Clubhouse_S03E31_-_The Golden Boo Boo [ukr_eng] WEB-DL 1080p [Hurtom].mkv")]
    [InlineData("The Penguins Of Madagascar - S01E25 - Roger Dodger WEBDL 1080p [Ukr,Eng] Sub [Eng] [ Hurtom ].mkv")]
    [InlineData("Show S03E09 (was S03E10b).mkv")]
    // A letter apart from the number is a word.
    [InlineData("S03E09 a.mkv")]
    // "Part 1" is never a segment: such an episode has a number of its own.
    [InlineData("The.Grand.Tour.S01E07. The Beach (Buggy) Boys - Part 1. 1080p V_odne_rilo.mkv")]
    public void Read_of_a_name_whose_first_marker_has_no_letter_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = EpisodeSegments.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData("S03E09a-b.mkv")]
    [InlineData("S03E09a+b.mkv")]
    [InlineData("S03E09a&b.mkv")]
    [InlineData("S03E09a,b.mkv")]
    [InlineData("S03E09a_b.mkv")]
    [InlineData("S03E09a–b.mkv")]
    [InlineData("S03E09a-E10.mkv")]
    [InlineData("S03E09a-10.mkv")]
    [InlineData("S03E09a-S03E09b.mkv")]
    // Multi-episode names: "e" is the next marker's, not a segment.
    [InlineData("S01E01E02.mkv")]
    [InlineData("S01E01-E02.mkv")]
    [InlineData("S03E09e.mkv")]
    public void Read_of_two_segments_or_a_range_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = EpisodeSegments.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData("S03E09A.mkv")]
    [InlineData("S01E05A Day at the Beach.mkv")]
    [InlineData("S01E05arrival.mkv")]
    [InlineData("S03E09a1080p.mkv")]
    [InlineData("S03E09ab.mkv")]
    public void Read_of_a_capital_letter_or_a_word_glued_to_the_number_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = EpisodeSegments.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData("S03 E09a.mkv")]
    [InlineData("S03.E09a.mkv")]
    [InlineData("Planet Earth E08a.mkv")]
    [InlineData("Show 1x09a.mkv")]
    [InlineData("THES03E09a.mkv")]
    public void Read_of_a_marker_not_written_SxxEyy_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = EpisodeSegments.Read(fileName);

        //Assert
        Assert.Null(read);
    }

    [Theory]
    [InlineData("[01] Blade Runner (1982) [The Final Cut] UHD-BDRip 1080p H.265 SDR [2xUKR_ENG] [Hurtom].mkv")]
    [InlineData("[03.2] Alien³ (1992) [Special Assembly Cut] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv")]
    [InlineData("99-2. The Egg and Jerry (1956) (Widescreen).mkv")]
    [InlineData("Polanski+Skolimowski o Noże.mkv")]
    public void Read_of_a_films_name_is_nothing(string fileName)
    {
        //Arrange

        //Act
        var read = EpisodeSegments.Read(fileName);

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
        var read = EpisodeSegments.Read(fileName);

        //Assert
        Assert.Null(read);
    }
}
