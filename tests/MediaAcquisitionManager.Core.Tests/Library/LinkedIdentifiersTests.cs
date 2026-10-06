using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34 — the one rule for "a linked id counts like a library id": a link to the work adds its TMDb id and, where the
// library has no IMDb id, TMDb's; the library id it replaces gives way only while the link isn't confirmed in the library
// (R6); any third value stays and conflicts; an episode-to-show link adds nothing and drops the wrong film id (R7).
public sealed class LinkedIdentifiersTests
{
    private static readonly MediaIdentifier LibraryImdb = new(MediaIdentifierScheme.Imdb, "tt0090605");

    [Fact]
    public void Effective_of_a_work_link_on_an_item_without_ids_adds_its_tmdb_and_imdb_ids()
    {
        //Arrange
        var link = WorkLink("223021", superseded: null, imdbId: "tt0056164", LibraryLinkWriteState.Written);

        //Act
        var effective = LinkedIdentifiers.Effective([], link);

        //Assert
        Assert.Equal(
            [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "223021"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0056164")],
            effective);
    }

    [Fact]
    public void Effective_keeps_the_librarys_imdb_id_over_the_one_tmdb_gave()
    {
        //Arrange
        // R8 — TMDb's IMDb id is projected only where the library's rows hold none.
        var link = WorkLink("679", superseded: null, imdbId: "tt9999999", LibraryLinkWriteState.KeptInApp);

        //Act
        var effective = LinkedIdentifiers.Effective([LibraryImdb], link);

        //Assert
        Assert.Equal([LibraryImdb, new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], effective);
    }

    [Fact]
    public void Effective_adds_the_linked_id_once_when_the_library_already_holds_it()
    {
        //Arrange
        var library = new MediaIdentifier(MediaIdentifierScheme.Tmdb, " 679 ");
        var link = WorkLink("679", superseded: null, imdbId: null, LibraryLinkWriteState.InLibrary);

        //Act
        var effective = LinkedIdentifiers.Effective([library], link);

        //Assert
        Assert.Equal([library], effective);
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.KeptInApp)]
    [InlineData(LibraryLinkWriteState.Queued)]
    [InlineData(LibraryLinkWriteState.Writing)]
    [InlineData(LibraryLinkWriteState.NotWritten)]
    public void Effective_drops_the_replaced_library_id_while_the_link_is_not_confirmed(LibraryLinkWriteState state)
    {
        //Arrange
        var wrong = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "999");
        var link = WorkLink("679", superseded: "999", imdbId: null, state);

        //Act
        var effective = LinkedIdentifiers.Effective([LibraryImdb, wrong], link);
        var conflicting = LinkedIdentifiers.ConflictingValue([LibraryImdb, wrong], link);

        //Assert
        Assert.Equal([LibraryImdb, new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], effective);
        Assert.Null(conflicting);
    }

    [Theory]
    [InlineData(LibraryLinkWriteState.Written)]
    [InlineData(LibraryLinkWriteState.InLibrary)]
    [InlineData(LibraryLinkWriteState.Lost)]
    public void Effective_keeps_the_replaced_value_once_the_link_was_confirmed_and_it_conflicts(LibraryLinkWriteState state)
    {
        //Arrange
        // R6 — the library took the link's id; holding the old one again is the library's word, and neither wins.
        var wrong = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "999");
        var link = WorkLink("679", superseded: "999", imdbId: null, state);

        //Act
        var effective = LinkedIdentifiers.Effective([wrong], link);
        var conflicting = LinkedIdentifiers.ConflictingValue([wrong], link);

        //Assert
        Assert.Equal([wrong, new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], effective);
        Assert.Equal("999", conflicting);
    }

    [Fact]
    public void Effective_keeps_a_third_library_value_and_it_conflicts()
    {
        //Arrange
        var third = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "555");
        var link = WorkLink("679", superseded: "999", imdbId: null, LibraryLinkWriteState.NotWritten);

        //Act
        var effective = LinkedIdentifiers.Effective([third], link);
        var conflicting = LinkedIdentifiers.ConflictingValue([third], link);

        //Assert
        Assert.Equal([third, new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], effective);
        Assert.Equal("555", conflicting);
    }

    [Fact]
    public void Effective_compares_the_replaced_value_trimmed_and_in_any_letter_case()
    {
        //Arrange
        var wrong = new MediaIdentifier(MediaIdentifierScheme.Imdb, " TT0090605 ");
        var link = new ItemLinkFacts(LibraryLinkTarget.Work, MediaIdentifierScheme.Imdb, "tt1234567", "tt0090605", null, LibraryLinkWriteState.KeptInApp);

        //Act
        var effective = LinkedIdentifiers.Effective([wrong], link);

        //Assert
        Assert.Equal([new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt1234567")], effective);
    }

    [Fact]
    public void Effective_of_a_show_link_leaves_the_items_other_schemes_alone()
    {
        //Arrange
        var tvdb = new MediaIdentifier(MediaIdentifierScheme.Tvdb, "341164");
        var otherTmdb = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "999");
        var link = new ItemLinkFacts(LibraryLinkTarget.Work, MediaIdentifierScheme.TmdbTv, "73586", null, "tt4236770", LibraryLinkWriteState.Written);

        //Act
        var effective = LinkedIdentifiers.Effective([tvdb, otherTmdb], link);
        var conflicting = LinkedIdentifiers.ConflictingValue([tvdb, otherTmdb], link);

        //Assert
        Assert.Equal(
        [
            tvdb, otherTmdb,
            new MediaIdentifier(MediaIdentifierScheme.TmdbTv, "73586"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt4236770"),
        ], effective);
        Assert.Null(conflicting);
    }

    [Fact]
    public void Effective_of_an_episode_link_adds_nothing_and_drops_the_wrong_film_id()
    {
        //Arrange
        // R7 — Planet Earth's E08, a "movie" Jellyfin gave a film id: the show's ids aren't the item's, and the film id
        // gives way to the link, which is never written, so always.
        var wrongFilm = new MediaIdentifier(MediaIdentifierScheme.Tmdb, "12345");
        var link = new ItemLinkFacts(LibraryLinkTarget.ShowEpisode, MediaIdentifierScheme.TmdbTv, "1044", "12345", "tt0795176", LibraryLinkWriteState.KeptInApp);

        //Act
        var effective = LinkedIdentifiers.Effective([LibraryImdb, wrongFilm], link);
        var conflicting = LinkedIdentifiers.ConflictingValue([LibraryImdb, wrongFilm], link);

        //Assert
        Assert.Equal([LibraryImdb], effective);
        Assert.Null(conflicting);
    }

    [Fact]
    public void Effective_of_an_episode_link_without_a_replaced_id_is_the_librarys_ids()
    {
        //Arrange
        var link = new ItemLinkFacts(LibraryLinkTarget.ShowEpisode, MediaIdentifierScheme.TmdbTv, "1044", null, "tt0795176", LibraryLinkWriteState.KeptInApp);

        //Act
        var effective = LinkedIdentifiers.Effective([LibraryImdb], link);

        //Assert
        Assert.Equal([LibraryImdb], effective);
    }

    [Theory]
    [InlineData(null, "12345", "12345")]
    [InlineData("100", "12345", "12345")]
    public void ConflictingValue_of_an_episode_link_is_a_film_id_the_library_gave_the_item_after_it(
        string? superseded, string libraryTmdbId, string expected)
    {
        //Arrange
        // A refresh or an Identify gave the linked episode a film id: it stays, and neither it nor the link wins.
        var laterFilm = new MediaIdentifier(MediaIdentifierScheme.Tmdb, libraryTmdbId);
        var link = new ItemLinkFacts(LibraryLinkTarget.ShowEpisode, MediaIdentifierScheme.TmdbTv, "1044", superseded, "tt0795176", LibraryLinkWriteState.KeptInApp);

        //Act
        var effective = LinkedIdentifiers.Effective([LibraryImdb, laterFilm], link);
        var conflicting = LinkedIdentifiers.ConflictingValue([LibraryImdb, laterFilm], link);

        //Assert
        Assert.Equal([LibraryImdb, laterFilm], effective);
        Assert.Equal(expected, conflicting);
    }

    [Fact]
    public void ConflictingValue_of_an_item_holding_the_linked_id_is_null()
    {
        //Arrange
        var link = WorkLink("679", superseded: null, imdbId: null, LibraryLinkWriteState.InLibrary);

        //Act
        var conflicting = LinkedIdentifiers.ConflictingValue([new MediaIdentifier(MediaIdentifierScheme.Tmdb, "679")], link);

        //Assert
        Assert.Null(conflicting);
    }

    private static ItemLinkFacts WorkLink(string value, string? superseded, string? imdbId, LibraryLinkWriteState state) =>
        new(LibraryLinkTarget.Work, MediaIdentifierScheme.Tmdb, value, superseded, imdbId, state);
}
