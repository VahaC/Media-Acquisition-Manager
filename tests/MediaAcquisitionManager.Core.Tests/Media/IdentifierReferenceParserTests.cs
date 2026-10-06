using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Core.Tests.Media;

// MAM-34 — what the owner pastes into a work's TMDb dialog: a TMDb film, show, season or episode link, an IMDb link or
// id, or a bare TMDb number, however it was copied; anything else is nothing rather than a guess.
public sealed class IdentifierReferenceParserTests
{
    [Theory]
    [InlineData("https://www.themoviedb.org/movie/223021-landing-stripling")]
    [InlineData("https://www.themoviedb.org/movie/223021-landing-stripling?language=uk-UA")]
    [InlineData("http://themoviedb.org/movie/223021")]
    [InlineData("themoviedb.org/movie/223021/")]
    [InlineData("www.themoviedb.org/movie/223021-landing-stripling#overview")]
    [InlineData("  HTTPS://WWW.THEMOVIEDB.ORG/MOVIE/223021-LANDING-STRIPLING  ")]
    [InlineData("https://www.themoviedb.org/movie/223021-landing-stripling/cast")]
    public void Parse_reads_a_tmdb_film_link_however_it_was_copied(string text)
    {
        //Arrange

        //Act
        var reference = IdentifierReferenceParser.Parse(text);

        //Assert
        Assert.Equal(new IdentifierReference(MediaIdentifierScheme.Tmdb, "223021"), reference);
    }

    [Theory]
    [InlineData("https://www.themoviedb.org/tv/1044-planet-earth", null, null)]
    [InlineData("themoviedb.org/tv/1044", null, null)]
    [InlineData("https://www.themoviedb.org/tv/1044/season/1/episode/8", 1, 8)]
    [InlineData("https://www.themoviedb.org/tv/1044-planet-earth/season/1/episode/8?language=uk-UA", 1, 8)]
    [InlineData("https://www.themoviedb.org/tv/1044-planet-earth/season/0", 0, null)]
    public void Parse_reads_a_tmdb_show_link_with_its_season_and_episode(string text, int? season, int? episode)
    {
        //Arrange

        //Act
        var reference = IdentifierReferenceParser.Parse(text);

        //Assert
        Assert.Equal(new IdentifierReference(MediaIdentifierScheme.TmdbTv, "1044", season, episode), reference);
    }

    [Theory]
    [InlineData("https://www.imdb.com/title/tt0056164/")]
    [InlineData("https://m.imdb.com/title/tt0056164/?ref_=nv_sr_srsg_0")]
    [InlineData("imdb.com/title/tt0056164")]
    [InlineData("https://www.imdb.com/uk/title/tt0056164/reference")]
    [InlineData("tt0056164")]
    [InlineData(" TT0056164 ")]
    public void Parse_reads_an_imdb_link_or_id_with_its_prefix_lower_cased(string text)
    {
        //Arrange

        //Act
        var reference = IdentifierReferenceParser.Parse(text);

        //Assert
        Assert.Equal(new IdentifierReference(MediaIdentifierScheme.Imdb, "tt0056164"), reference);
    }

    [Fact]
    public void Parse_of_a_bare_number_leaves_the_scheme_to_the_owner()
    {
        //Arrange
        // TMDb numbers films and shows apart (ADR 0017): 1044 is a show and a film.

        //Act
        var reference = IdentifierReferenceParser.Parse(" 223021 ");

        //Assert
        Assert.Equal(new IdentifierReference(null, "223021"), reference);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Landing Stripling")]
    [InlineData("https://www.themoviedb.org/person/24-robert-zemeckis")]
    [InlineData("https://www.themoviedb.org/collection/284433")]
    [InlineData("https://www.themoviedb.org/movie/landing-stripling")]
    [InlineData("https://www.themoviedb.org/movie/1234567890123")]
    [InlineData("https://www.themoviedb.org/tv/1044/season/one")]
    [InlineData("https://www.imdb.com/name/nm0001661/")]
    [InlineData("https://www.imdb.com/title/tt12/")]
    [InlineData("https://www.themoviedb.org.example.com/movie/223021")]
    [InlineData("https://example.com/themoviedb.org/movie/223021")]
    [InlineData("ftp://www.themoviedb.org/movie/223021")]
    [InlineData("https://user@www.themoviedb.org/movie/223021")]
    [InlineData("tt0056164x")]
    [InlineData("22 3021")]
    [InlineData("٢٢٣٠٢١")]
    public void Parse_of_anything_else_is_nothing(string? text)
    {
        //Arrange

        //Act
        var reference = IdentifierReferenceParser.Parse(text);

        //Assert
        Assert.Null(reference);
    }
}
