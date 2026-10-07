using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-20 — the editions a file's name or a version's name says a copy is: a distinction between copies the Duplicates
// list flags for review, never a match (ADR 0012, ADR 0028). Every word of the vocabulary in English and Ukrainian, the
// ways file names write it, whole words only, and the real names of the reference library.
public sealed class EditionLabelsTests
{
    private static readonly MediaEdition[] None = [];

    [Theory]
    [InlineData("Theatrical", MediaEdition.Theatrical)]
    [InlineData("Theatrical Cut", MediaEdition.Theatrical)]
    [InlineData("Theatrical Edition", MediaEdition.Theatrical)]
    [InlineData("Theatrical Version", MediaEdition.Theatrical)]
    [InlineData("театральна версія", MediaEdition.Theatrical)]
    [InlineData("кінотеатральна версія", MediaEdition.Theatrical)]
    [InlineData("прокатна версія", MediaEdition.Theatrical)]
    [InlineData("Director's Cut", MediaEdition.DirectorsCut)]
    [InlineData("Directors Cut", MediaEdition.DirectorsCut)]
    [InlineData("Director Cut", MediaEdition.DirectorsCut)]
    [InlineData("Director's Definitive Cut", MediaEdition.DirectorsCut)]
    [InlineData("режисерська версія", MediaEdition.DirectorsCut)]
    [InlineData("режисерська", MediaEdition.DirectorsCut)]
    [InlineData("Extended", MediaEdition.Extended)]
    [InlineData("Extended Cut", MediaEdition.Extended)]
    [InlineData("Extended Edition", MediaEdition.Extended)]
    [InlineData("Extended Version", MediaEdition.Extended)]
    [InlineData("розширена версія", MediaEdition.Extended)]
    [InlineData("розширена", MediaEdition.Extended)]
    [InlineData("Unrated", MediaEdition.Unrated)]
    [InlineData("Uncut", MediaEdition.Uncut)]
    [InlineData("без цензури", MediaEdition.Uncut)]
    [InlineData("Remaster", MediaEdition.Remastered)]
    [InlineData("Remastered", MediaEdition.Remastered)]
    [InlineData("ремастер", MediaEdition.Remastered)]
    [InlineData("ремастеринг", MediaEdition.Remastered)]
    [InlineData("Special Edition", MediaEdition.SpecialEdition)]
    [InlineData("IMAX", MediaEdition.Imax)]
    [InlineData("Open Matte", MediaEdition.OpenMatte)]
    [InlineData("Final Cut", MediaEdition.FinalCut)]
    [InlineData("Redux", MediaEdition.Redux)]
    [InlineData("International Cut", MediaEdition.International)]
    [InlineData("International Version", MediaEdition.International)]
    [InlineData("Alternate Cut", MediaEdition.Alternate)]
    [InlineData("Alternate Version", MediaEdition.Alternate)]
    [InlineData("Alternative Cut", MediaEdition.Alternate)]
    [InlineData("Alternative Version", MediaEdition.Alternate)]
    [InlineData("Assembly Cut", MediaEdition.Assembly)]
    [InlineData("Widescreen", MediaEdition.Widescreen)]
    [InlineData("Fullscreen", MediaEdition.FullScreen)]
    [InlineData("Full Screen", MediaEdition.FullScreen)]
    public void Read_names_every_edition_of_the_vocabulary(string text, MediaEdition expected)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Equal([expected], editions);
    }

    [Theory]
    [InlineData("director's cut")]
    [InlineData("DIRECTOR'S CUT")]
    [InlineData("DiReCtOrS cUt")]
    [InlineData("РЕЖИСЕРСЬКА ВЕРСІЯ")]
    [InlineData("Режисерська Версія")]
    public void Read_takes_an_edition_in_any_letter_case(string text)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Equal([MediaEdition.DirectorsCut], editions);
    }

    [Theory]
    // The typewriter apostrophe, the right single quote, the modifier letter Ukrainian spelling recommends, and none.
    [InlineData("Director's Cut")]
    [InlineData("Director’s Cut")]
    [InlineData("Directorʼs Cut")]
    [InlineData("Directors Cut")]
    public void Read_takes_the_apostrophe_of_directors_cut_written_any_way_or_left_out(string text)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Equal([MediaEdition.DirectorsCut], editions);
    }

    [Theory]
    // The ways file names separate words: dots, underscores, dashes, and runs of them.
    [InlineData("Aliens.1986.Directors.Cut.1080p.mkv", MediaEdition.DirectorsCut)]
    [InlineData("Aliens_1986_Directors_Cut_1080p.mkv", MediaEdition.DirectorsCut)]
    [InlineData("Aliens-1986-Directors-Cut-1080p.mkv", MediaEdition.DirectorsCut)]
    [InlineData("Aliens 1986 Director's - Cut", MediaEdition.DirectorsCut)]
    [InlineData("Aliens.1986.Special.Edition.mkv", MediaEdition.SpecialEdition)]
    [InlineData("Movie_Open_Matte_2160p", MediaEdition.OpenMatte)]
    [InlineData("Movie.Full.Screen.DVDRip", MediaEdition.FullScreen)]
    [InlineData("Movie-Extended-Edition-BDRip", MediaEdition.Extended)]
    [InlineData("Фільм.Режисерська.Версія.mkv", MediaEdition.DirectorsCut)]
    [InlineData("Фільм_без_цензури_1080p", MediaEdition.Uncut)]
    public void Read_takes_dots_underscores_and_dashes_between_the_words_of_an_edition(string text, MediaEdition expected)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Equal([expected], editions);
    }

    [Theory]
    // A label is a whole word: letters or digits on either side make it part of another word.
    [InlineData("Extendedly")]
    [InlineData("Overextended")]
    [InlineData("Theatricals")]
    [InlineData("Uncutting")]
    [InlineData("Remasteredness")]
    [InlineData("Imaxed")]
    [InlineData("IMAX3D")]
    [InlineData("Reduxes")]
    [InlineData("Widescreens")]
    [InlineData("Special Editions")]
    [InlineData("Directors Cutting")]
    [InlineData("нерозширена")]
    // A word that is an edition only beside another stays a title's word alone.
    [InlineData("Special Forces (2011)")]
    [InlineData("Final Destination (2000)")]
    [InlineData("The International (2009)")]
    [InlineData("Interstellar (2014) BDRemux 1080p")]
    public void Read_never_takes_an_edition_word_inside_another_word_or_alone_where_it_needs_two(string text)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Empty(editions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_of_no_text_names_no_edition(string? text)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Empty(editions);
    }

    [Fact]
    public void Read_of_unrated_directors_cut_names_both_editions()
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read("Unrated Director's Cut");

        //Assert
        Assert.Equal([MediaEdition.DirectorsCut, MediaEdition.Unrated], editions);
    }

    [Fact]
    public void Read_names_the_editions_in_the_enums_order_whatever_the_texts()
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read("Full Screen IMAX Remastered Theatrical Cut");

        //Assert
        Assert.Equal([MediaEdition.Theatrical, MediaEdition.Remastered, MediaEdition.Imax, MediaEdition.FullScreen], editions);
    }

    [Fact]
    public void Read_names_an_edition_said_several_times_once()
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read("Director's Cut / Directors.Cut / режисерська версія");

        //Assert
        Assert.Equal([MediaEdition.DirectorsCut], editions);
    }

    /// <summary>Folder and version names of the reference library, and the shapes around them.</summary>
    public static TheoryData<string, MediaEdition[]> ReferenceNames() => new()
    {
        { "Alien (1979) [Director's Cut] BDRemux 1080p", [MediaEdition.DirectorsCut] },
        { "Terminator 2 (1991) [UK Remastered, Theatrical Cut]", [MediaEdition.Theatrical, MediaEdition.Remastered] },
        { "99-2. The Egg and Jerry (1956) (Widescreen)", [MediaEdition.Widescreen] },
        { "Tropic Thunder (2008) [Unrated Director's Cut]", [MediaEdition.DirectorsCut, MediaEdition.Unrated] },
        { "The Lord of the Rings: The Fellowship of the Ring (2001) Extended Edition", [MediaEdition.Extended] },
        { "Blade Runner (1982) The Final Cut", [MediaEdition.FinalCut] },
        { "Чужі / Aliens (1986) [Режисерська версія] BDRip", [MediaEdition.DirectorsCut] },
        { "Alien (1979) BDRemux 1080p", None },
    };

    [Theory]
    [MemberData(nameof(ReferenceNames))]
    public void Read_names_the_editions_of_the_reference_librarys_names(string text, MediaEdition[] expected)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text);

        //Assert
        Assert.Equal(expected, editions);
    }

    /// <summary>A name, the titles of its work, and the editions the name says besides them.</summary>
    public static TheoryData<string, string[], MediaEdition[]> NamesWithTitles() => new()
    {
        // The title's own word is no cut, however the name separates it.
        { "Uncut.Gems.2019.1080p.mkv", ["Uncut Gems", "Неограненные алмазы"], None },
        { "uncut_gems_2019", ["Uncut Gems"], None },
        { "The Final Cut (2004).mkv", ["The Final Cut"], None },
        // A cut the name says beside the title is still read.
        { "Uncut Gems (2019) Uncut.mkv", ["Uncut Gems"], [MediaEdition.Uncut] },
        { "Blade.Runner.1982.The.Final.Cut.1080p.mkv", ["Blade Runner"], [MediaEdition.FinalCut] },
        // Every title that names an edition goes, one holding another too; the cut written after them stays.
        { "Blade Runner The Final Cut Director's Cut", ["The Final Cut", "Blade Runner: The Final Cut"], [MediaEdition.DirectorsCut] },
        // A title that names no edition is left in the text; a name without the title is read whole.
        { "Alien (1979) [Director's Cut]", ["Alien", "Uncut Gems"], [MediaEdition.DirectorsCut] },
        { "Неограненные алмазы (2019) Extended.mkv", ["Uncut Gems"], [MediaEdition.Extended] },
    };

    [Theory]
    [MemberData(nameof(NamesWithTitles))]
    public void Read_with_the_works_titles_takes_no_edition_from_a_titles_words(string text, string[] titles, MediaEdition[] expected)
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(text, titles);

        //Assert
        Assert.Equal(expected, editions);
    }

    [Fact]
    public void Read_with_the_works_titles_of_no_text_names_no_edition()
    {
        //Arrange

        //Act
        var editions = EditionLabels.Read(null, ["Uncut Gems"]);

        //Assert
        Assert.Empty(editions);
    }
}
