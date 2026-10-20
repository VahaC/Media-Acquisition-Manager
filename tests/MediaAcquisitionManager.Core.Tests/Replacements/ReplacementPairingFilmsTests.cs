using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Replacements.ReplacementPairingInputs;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — check 3 and the pairs for films (ADR 0032, ADR 0035): a film's new item must carry a TMDb or IMDb id of the old
// work — the owner's own TMDb link counts — and no other id of a scheme the work has; conflicting ids, ids of two works,
// ids for a work that has none, a film item in a TV job, two new files of one work and no new file of a ticked copy's work
// stop the job; no id yet, or ids only of a scheme the work has none of, waits until the hour. Another work's id stops it
// too while a ticked copy found no new file; once every one did, that film is a collection's other film, simply added as
// an extra is (Vasyl, 2026-10-10). A paired file whose name claims another edition than the release — any title of the
// work taken out first —, or that runs under three quarters of its old copy (exactly three quarters, a longer cut and an
// unmeasured old copy pass), stops it. The matched works are those a copy is ticked of, as the job's rows hold them.
public sealed class ReplacementPairingFilmsTests
{
    private const string OldAlien = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
    private const string NewAlien = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";
    private const string NewAliens = "/media/movies/Aliens (1986)/Aliens.1986.2160p.mkv";

    [Theory]
    [InlineData(MediaIdentifierScheme.Tmdb, "348")]
    [InlineData(MediaIdentifierScheme.Imdb, "tt0078748")]
    public void Pair_of_a_film_whose_new_item_carries_the_old_works_id_passes_with_its_pair(MediaIdentifierScheme scheme, string value)
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(scheme, value)));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal((ReplacementPairingVerdict.Pass, (string?)null), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, null)], result.OldCopies.InEnglish());
        Assert.Equal([new EnglishNewFileOutcome(file.Id, Alien.WorkId, null)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_film_whose_new_item_carries_the_owners_own_tmdb_link_passes()
    {
        //Arrange
        // ADR 0026: the owner's TMDb link counts as the old work's id, beside the one the library reported.
        var alien = Alien with { Identifiers = [.. Alien.Identifiers, Id(MediaIdentifierScheme.Tmdb, "999")] };
        var copy = FilmCopy(0, alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "999")));

        //Act
        var result = ReplacementPairing.Pair([alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_compares_ids_trimmed_and_in_any_letter_case()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, " TT0078748 "), Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_of_a_new_item_with_an_id_of_a_scheme_the_work_lacks_agrees_on_the_other()
    {
        //Arrange
        // A TVDB id on a film item names nothing Alien's ids could contradict.
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0078748"), Id(MediaIdentifierScheme.Tvdb, "123")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_of_a_new_item_identified_as_another_work_stops_for_review_with_its_ids()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        // The file is the reason, not the copy left without one: fixing the file's item may pair it.
        const string reason =
            "“Jellyfin” identified “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” as another work (IMDb tt0090605, TMDb 679), not Alien (1979).";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, reason)], result.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_new_item_whose_ids_share_no_scheme_with_the_works_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        // TMDb 348 compares with nothing of a work known only by IMDb: it is no other work's id, so it waits as a file
        // without an id does — the library may still give the item its IMDb id, or the owner fix it.
        var alien = Alien with { Identifiers = [Id(MediaIdentifierScheme.Imdb, "tt0078748")] };
        var copy = FilmCopy(0, alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var waiting = ReplacementPairing.Pair([alien], [copy], [file], [], "Living room", libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([alien], [copy], [file], [], "Living room", libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Living room” gave “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” TMDb 348 within an hour, but Alien (1979) is known here only by IMDb tt0078748.";
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (waiting.Verdict, TestTexts.En(waiting.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, null)], waiting.NewFiles.InEnglish());
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (stopped.Verdict, TestTexts.En(stopped.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, reason)], stopped.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], stopped.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_new_item_with_ids_for_a_work_without_any_stops_for_review_at_once()
    {
        //Arrange
        // A work the owner confirmed by its title and year alone: no id the library gives could ever show the file is it.
        var alien = Alien with { Identifiers = [] };
        var copy = FilmCopy(0, alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348"), Id(MediaIdentifierScheme.Imdb, "tt0078748")));

        //Act
        var result = ReplacementPairing.Pair([alien], [copy], [file], [], "Living room", libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Living room” gave “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” IMDb tt0078748 and TMDb 348, but Alien (1979) has no TMDb or IMDb id here to compare them with.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, reason)], result.NewFiles.InEnglish());
    }

    [Theory]
    // MAM-47 — "has"/"have" and "it"/"them" became a key per shape (no-id-to-compare.film.*): each shape keeps its English.
    [InlineData(false, false, "TMDb 348, but Alien (1979) has no TMDb or IMDb id here to compare it with.")]
    [InlineData(true, false, "TMDb 348, but Alien (1979) and Aliens (1986) have no TMDb or IMDb id here to compare it with.")]
    [InlineData(true, true, "IMDb tt0078748 and TMDb 348, but Alien (1979) and Aliens (1986) have no TMDb or IMDb id here to compare them with.")]
    public void Pair_of_a_new_item_with_ids_for_works_without_any_words_the_works_and_ids_by_their_number(bool twoWorks, bool twoIds, string tail)
    {
        //Arrange
        var alien = Alien with { Identifiers = [] };
        var aliens = Aliens with { Identifiers = [] };
        ReplacementPairingWork[] works = twoWorks ? [alien, aliens] : [alien];
        ReplacementPairingOldCopy[] copies = twoWorks
            ? [FilmCopy(0, alien, OldAlien), FilmCopy(1, aliens, "/media/movies/Aliens (1986)/Aliens.1986.1080p.mkv")]
            : [FilmCopy(0, alien, OldAlien)];
        MediaIdentifier[] ids = twoIds
            ? [Id(MediaIdentifierScheme.Tmdb, "348"), Id(MediaIdentifierScheme.Imdb, "tt0078748")]
            : [Id(MediaIdentifierScheme.Tmdb, "348")];
        var file = NewFile(0, NewAlien, FilmItem(ids));

        //Act
        var result = ReplacementPairing.Pair(works, copies, [file], [], "Living room", libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review, $"“Living room” gave “{NewAlien}” {tail}"),
            (result.Verdict, TestTexts.En(result.Reason)));
    }

    [Fact]
    public void Pair_of_a_new_item_differing_in_the_one_scheme_the_work_has_is_another_work()
    {
        //Arrange
        // Another IMDb id says it is another work; the TMDb id beside it changes nothing.
        var alien = Alien with { Identifiers = [Id(MediaIdentifierScheme.Imdb, "tt0078748")] };
        var copy = FilmCopy(0, alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” identified “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” as another work (IMDb tt0090605, TMDb 348), not Alien (1979)."),
            (result.Verdict, TestTexts.En(result.Reason)));
    }

    [Fact]
    public void Pair_of_a_new_item_unlike_one_matched_film_and_not_comparable_with_the_other_waits()
    {
        //Arrange
        // TMDb 348 is not Aliens (TMDb 679), but may yet be Alien, known here only by IMDb: no work is proven.
        var alien = Alien with { Identifiers = [Id(MediaIdentifierScheme.Imdb, "tt0078748")] };
        var alienCopy = FilmCopy(0, alien, OldAlien);
        var aliensCopy = FilmCopy(1, Aliens, "/media/movies/Aliens (1986)/Aliens.1986.1080p.mkv");
        var unclear = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var aliens = NewFile(1, NewAliens, FilmItem(Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var waiting = ReplacementPairing.Pair([alien, Aliens], [alienCopy, aliensCopy], [unclear, aliens], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([alien, Aliens], [alienCopy, aliensCopy], [unclear, aliens], [], LibraryName, libraryWaitPassed: true);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Wait, waiting.Verdict);
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” gave “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” TMDb 348 within an hour, but Alien (1979) is known here only by IMDb tt0078748."),
            (stopped.Verdict, TestTexts.En(stopped.Reason)));
    }

    [Fact]
    public void Pair_of_a_new_item_sharing_one_id_with_the_work_and_differing_in_another_stops_for_conflicting_ids()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348"), Id(MediaIdentifierScheme.Imdb, "tt0090605")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” gave “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” conflicting ids: TMDb 348 is Alien (1979)'s, IMDb tt0090605 is not."),
            (result.Verdict, TestTexts.En(result.Reason)));
    }

    [Fact]
    public void Pair_of_a_new_item_whose_ids_fit_two_matched_films_stops_for_review()
    {
        //Arrange
        // Two matched works each holding one of the item's ids, neither contradicted: the ids can't tell them apart.
        var byImdb = Film(Guid.NewGuid(), "Alien (1979)", ["Alien"], Id(MediaIdentifierScheme.Imdb, "tt0078748"));
        var byTmdb = Film(Guid.NewGuid(), "Alien: Director's Cut (2003)", ["Alien"], Id(MediaIdentifierScheme.Tmdb, "348"));
        var imdbCopy = FilmCopy(0, byImdb, OldAlien);
        var tmdbCopy = FilmCopy(1, byTmdb, "/media/movies/Alien (2003)/Alien.2003.Directors.Cut.1080p.mkv");
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0078748"), Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([byImdb, byTmdb], [imdbCopy, tmdbCopy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin”'s ids for “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” fit both Alien (1979) and Alien: Director's Cut (2003), so the app can't tell which work it is."),
            (result.Verdict, TestTexts.En(result.Reason)));
    }

    [Fact]
    public void Pair_of_a_new_item_without_a_tmdb_or_imdb_id_waits_until_the_hour()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tvdb, "123")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        // The owner may fix the item in the library meanwhile; the check then passes by itself.
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, null)], result.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_new_item_without_an_id_once_the_hour_ran_out_stops_for_review()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem());

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Jellyfin” gave “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” no TMDb or IMDb id within an hour, so it can't be told to be Alien (1979).";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, reason)], result.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_film_item_in_a_job_of_episodes_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, "/media/tv-shows/Penguins/Season 02/Penguins.S02E05.mkv");
        var file = NewFile(0, "/media/tv-shows/Penguins/Penguins.S02E05.mkv", FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0892700")));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        // A work that is a movie item on one side and an episode on the other.
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” made “/media/tv-shows/Penguins/Penguins.S02E05.mkv” a film, while the old copies are episodes of The Penguins of Madagascar."),
            (result.Verdict, TestTexts.En(result.Reason)));
        // MAM-47 — one series has a key of its own, so Ukrainian names it in the singular.
        Assert.Equal("replacements.pairing.made-film-of-series", result.Reason?.Key);
        Assert.Equal(
            "Медіатека «Jellyfin» визначила файл «/media/tv-shows/Penguins/Penguins.S02E05.mkv» як фільм, тоді як старі копії — це серії серіалу The Penguins of Madagascar.",
            TestTexts.Uk(result.Reason));
    }

    [Fact]
    public void Pair_of_a_film_item_in_a_job_of_two_series_names_both_series_in_the_plural()
    {
        //Arrange
        var doctorWho = Show(new Guid("b1000000-0000-0000-0000-000000000002"), "Doctor Who", Id(MediaIdentifierScheme.TmdbTv, "57243"));
        var copy = EpisodeCopy(0, 2, 5, "/media/tv-shows/Penguins/Season 02/Penguins.S02E05.mkv");
        var file = NewFile(0, "/media/tv-shows/Penguins/Penguins.S02E05.mkv", FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0892700")));

        //Act
        var result = ReplacementPairing.Pair([Penguins, doctorWho], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal("replacements.pairing.made-film", result.Reason?.Key);
        Assert.Equal(
            "“Jellyfin” made “/media/tv-shows/Penguins/Penguins.S02E05.mkv” a film, while the old copies are episodes of The Penguins of Madagascar and Doctor Who.",
            TestTexts.En(result.Reason));
        Assert.Equal(
            "Медіатека «Jellyfin» визначила файл «/media/tv-shows/Penguins/Penguins.S02E05.mkv» як фільм, тоді як старі копії — це серії серіалів The Penguins of Madagascar і Doctor Who.",
            TestTexts.Uk(result.Reason));
    }

    [Fact]
    public void Pair_of_two_new_files_of_one_film_stops_for_review_and_pairs_neither()
    {
        //Arrange
        // An unclear pair from a multi-file torrent: two versions of the film in one download.
        var copy = FilmCopy(0, Alien, OldAlien);
        var first = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var second = NewFile(1, "/media/movies/Alien (1979)/Alien.1979.1080p.REMUX.mkv", FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0078748")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [second, first], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "Two new files are Alien (1979): “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” and “/media/movies/Alien (1979)/Alien.1979.1080p.REMUX.mkv”; the app can't tell which one replaces the old copy.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason)], result.OldCopies.InEnglish());
        Assert.Equal([(first.Id, (Guid?)Alien.WorkId), (second.Id, Alien.WorkId)], result.NewFiles.Select(outcome => (outcome.NewFileId, outcome.WorkId)));
    }

    [Fact]
    public void Pair_of_a_ticked_film_no_new_file_is_stops_for_review()
    {
        //Arrange
        // Nothing else in the download that could be it: the copy itself says why.
        var copy = FilmCopy(0, Alien, OldAlien);
        var extra = NewFile(0, "/media/movies/Alien (1979)/Extras/Making of.mkv", OtherItem());

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [extra], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "No new file is Alien (1979), so “/media/movies/Alien (1979)/Alien.1979.1080p.mkv” has nothing to replace it.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason)], result.OldCopies.InEnglish());
        Assert.Equal([new EnglishNewFileOutcome(extra.Id, null, null)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_ticked_film_with_no_title_names_it_by_its_label_and_year()
    {
        //Arrange
        // MAM-47 WP12 — a work the library gave no title: named by a label of ours in the reader's language, never by
        // English words passed as its name.
        var untitled = new ReplacementPairingWork(
            new Guid("a1000000-0000-0000-0000-000000000009"), MediaWorkKind.Movie, ReplacementPairingWork.NameOf(null, 2005), [], [Id(MediaIdentifierScheme.Tmdb, "999")]);
        var copy = FilmCopy(0, untitled, "/media/movies/Unknown/Unknown.2005.mkv");
        var extra = NewFile(0, "/media/movies/Unknown/Extras/Making of.mkv", OtherItem());

        //Act
        var result = ReplacementPairing.Pair([untitled], [copy], [extra], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review, "No new file is a work with no title (2005), so “/media/movies/Unknown/Unknown.2005.mkv” has nothing to replace it."),
            (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal(Message.Of("replacements.pairing.work.untitled-year", ("year", "2005")), result.Reason!.Values["work"]);
    }

    [Fact]
    public void Pair_of_a_new_item_without_an_id_names_a_work_with_no_title_among_the_others()
    {
        //Arrange
        var untitled = new ReplacementPairingWork(
            new Guid("a1000000-0000-0000-0000-000000000009"), MediaWorkKind.Movie, ReplacementPairingWork.NameOf(null, null), [], [Id(MediaIdentifierScheme.Tmdb, "999")]);
        var alienCopy = FilmCopy(0, Alien, OldAlien);
        var untitledCopy = FilmCopy(1, untitled, "/media/movies/Unknown/Unknown.mkv");
        var file = NewFile(0, NewAlien, FilmItem());

        //Act
        var result = ReplacementPairing.Pair([Alien, untitled], [alienCopy, untitledCopy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Jellyfin” gave “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” no TMDb or IMDb id within an hour, so it can't be told to be Alien (1979) or a work with no title.";
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, reason)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_pairs_each_film_with_its_own_new_file()
    {
        //Arrange
        var alienCopy = FilmCopy(0, Alien, OldAlien);
        var aliensCopy = FilmCopy(1, Aliens, "/media/movies/Aliens (1986)/Aliens.1986.1080p.mkv");
        var alien = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var aliens = NewFile(1, NewAliens, FilmItem(Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var result = ReplacementPairing.Pair([Alien, Aliens], [aliensCopy, alienCopy], [aliens, alien], [], LibraryName, libraryWaitPassed: false);

        //Assert
        // A film from a collection pack is paired the same way as an episode from a season pack; outcomes by place.
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
        Assert.Equal([new EnglishOldCopyOutcome(alienCopy.Id, alien.Id, null), new EnglishOldCopyOutcome(aliensCopy.Id, aliens.Id, null)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_whose_ticked_film_paired_simply_adds_the_film_of_another_work_and_an_extra()
    {
        //Arrange
        // Only Alien is ticked, so only Alien is a matched work, as the job's rows hold them. Vasyl, 2026-10-10: once it
        // found its new file, the pack's Aliens — another work by the library's ids — is the collection's other film,
        // simply added as a season pack's extra episode is.
        var copy = FilmCopy(0, Alien, OldAlien);
        var alien = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var aliens = NewFile(1, NewAliens, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "679")));
        // An extra the library hasn't even finished reading holds nothing up.
        var extra = NewFile(2, "/media/movies/Alien (1979)/Extras/Making of.mkv", OtherItem(refreshed: false));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [alien, aliens, extra], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal((ReplacementPairingVerdict.Pass, (string?)null), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, alien.Id, null)], result.OldCopies.InEnglish());
        Assert.Equal(
            [new EnglishNewFileOutcome(alien.Id, Alien.WorkId, null), new EnglishNewFileOutcome(aliens.Id, null, null), new EnglishNewFileOutcome(extra.Id, null, null)],
            result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_without_the_ticked_films_new_file_stops_with_the_other_films_sentence()
    {
        //Arrange
        // Alien's new file isn't there: Aliens may be it, misidentified, so it is the reason, not the copy left without one.
        var copy = FilmCopy(0, Alien, OldAlien);
        var aliens = NewFile(0, NewAliens, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [aliens], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” identified “/media/movies/Aliens (1986)/Aliens.1986.2160p.mkv” as another work (IMDb tt0090605, TMDb 679), not Alien (1979).";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(aliens.Id, null, reason)], result.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_whose_ticked_films_new_file_is_identified_as_another_work_too_stops_with_the_first_sentence()
    {
        //Arrange
        // The library took Alien's new file for Aliens as well: no ticked copy paired, so neither film is simply added.
        var copy = FilmCopy(0, Alien, OldAlien);
        var misread = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605")));
        var aliens = NewFile(1, NewAliens, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [aliens, misread], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string misreadReason =
            "“Jellyfin” identified “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” as another work (IMDb tt0090605), not Alien (1979).";
        const string aliensReason =
            "“Jellyfin” identified “/media/movies/Aliens (1986)/Aliens.1986.2160p.mkv” as another work (IMDb tt0090605, TMDb 679), not Alien (1979).";
        Assert.Equal((ReplacementPairingVerdict.Review, misreadReason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(misread.Id, null, misreadReason), new EnglishNewFileOutcome(aliens.Id, null, aliensReason)], result.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_where_one_ticked_film_of_two_found_no_new_file_stops_with_the_other_films_sentence()
    {
        //Arrange
        // Alien and Aliens ticked, Alien paired, Aliens' new file missing: Alien³ may be it, so it isn't simply added yet.
        var alienCopy = FilmCopy(0, Alien, OldAlien);
        var aliensCopy = FilmCopy(1, Aliens, "/media/movies/Aliens (1986)/Aliens.1986.1080p.mkv");
        var alien = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var alien3 = NewFile(1, "/media/movies/Alien 3 (1992)/Alien3.1992.2160p.mkv", FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0103644"), Id(MediaIdentifierScheme.Tmdb, "8077")));

        //Act
        var result = ReplacementPairing.Pair([Alien, Aliens], [alienCopy, aliensCopy], [alien, alien3], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” identified “/media/movies/Alien 3 (1992)/Alien3.1992.2160p.mkv” as another work (IMDb tt0103644, TMDb 8077), not Alien (1979) or Aliens (1986).";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(alienCopy.Id, alien.Id, null), new EnglishOldCopyOutcome(aliensCopy.Id, null, null)], result.OldCopies.InEnglish());
        Assert.Equal([new EnglishNewFileOutcome(alien.Id, Alien.WorkId, null), new EnglishNewFileOutcome(alien3.Id, null, reason)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_whose_other_film_has_conflicting_ids_stops_for_review_though_the_ticked_film_paired()
    {
        //Arrange
        // Aliens' IMDb id beside Alien's TMDb id is no other work, but ids that contradict each other.
        var copy = FilmCopy(0, Alien, OldAlien);
        var alien = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var conflicting = NewFile(1, NewAliens, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [alien, conflicting], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” gave “/media/movies/Aliens (1986)/Aliens.1986.2160p.mkv” conflicting ids: TMDb 348 is Alien (1979)'s, IMDb tt0090605 is not.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, alien.Id, null)], result.OldCopies.InEnglish());
        Assert.Equal([new EnglishNewFileOutcome(alien.Id, Alien.WorkId, null), new EnglishNewFileOutcome(conflicting.Id, null, reason)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_collection_pack_whose_pair_stops_for_its_edition_still_simply_adds_the_film_of_another_work()
    {
        //Arrange
        // Alien found its new file, which stops the job for its own reason: the other film is still just added.
        var copy = FilmCopy(0, Alien, OldAlien);
        var extended = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.Extended.2160p.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var aliens = NewFile(1, NewAliens, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [extended, aliens], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "“/media/movies/Alien (1979)/Alien.1979.Extended.2160p.mkv” claims Extended cut, while the release claimed none.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, extended.Id, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
        Assert.Equal([new EnglishNewFileOutcome(extended.Id, Alien.WorkId, null), new EnglishNewFileOutcome(aliens.Id, null, null)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_of_a_new_file_whose_name_claims_an_edition_the_release_did_not_stops_for_review_keeping_the_pair()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.Extended.2160p.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "“/media/movies/Alien (1979)/Alien.1979.Extended.2160p.mkv” claims Extended cut, while the release claimed none.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_new_file_claiming_another_edition_than_the_release_names_both()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.Theatrical.Remastered.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [MediaEdition.DirectorsCut, MediaEdition.Remastered], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/movies/Alien (1979)/Alien.1979.Theatrical.Remastered.mkv” claims Theatrical cut and Remastered, while the release claimed Director's cut and Remastered.",
            TestTexts.En(result.Reason));
    }

    [Fact]
    public void Pair_names_every_edition_by_the_label_the_Duplicates_page_gives_it()
    {
        //Arrange
        // MAM-47 — the labels are common.edition.* keys picked by a switch that throws for a member it doesn't name: every
        // member of MediaEdition, a new one included, must have its label, and each label keeps its English.
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.Theatrical.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        MediaEdition[] release = [.. Enum.GetValues<MediaEdition>().Where(edition => edition != MediaEdition.Theatrical)];

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], release, LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/movies/Alien (1979)/Alien.1979.Theatrical.mkv” claims Theatrical cut, while the release claimed Director's cut, "
                + "Extended cut, Unrated, Uncut, Remastered, Special edition, IMAX, Open matte, Final cut, Redux, International cut, "
                + "Alternate cut, Assembly cut, Widescreen and Full screen.",
            TestTexts.En(result.Reason));
    }

    [Theory]
    // No edition in the name: nothing claimed, nothing to contradict.
    [InlineData("Alien.1979.2160p.mkv")]
    // Editions the release claimed too.
    [InlineData("Alien.1979.Directors.Cut.2160p.mkv")]
    [InlineData("Alien.1979.Remastered.Director's.Cut.mkv")]
    public void Pair_of_a_new_file_claiming_no_edition_the_release_did_not_passes(string fileName)
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, $"/media/movies/Alien (1979)/{fileName}", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [MediaEdition.DirectorsCut, MediaEdition.Remastered], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_reads_no_edition_from_the_works_own_title()
    {
        //Arrange
        var gems = Film(Guid.NewGuid(), "Uncut Gems (2019)", ["Uncut Gems"], Id(MediaIdentifierScheme.Tmdb, "473033"));
        var copy = FilmCopy(0, gems, "/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.1080p.mkv");
        var file = NewFile(0, "/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.2160p.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "473033")));

        //Act
        var result = ReplacementPairing.Pair([gems], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_reads_no_edition_from_any_title_of_the_work()
    {
        //Arrange
        // Shown by its Ukrainian title, the work goes by its original one too, which the file's name holds (MAM-37's rule).
        var gems = Film(Guid.NewGuid(), "Необроблені алмази (2019)", ["Необроблені алмази", "Uncut Gems"], Id(MediaIdentifierScheme.Tmdb, "473033"));
        var shownOnly = gems with { Titles = ["Необроблені алмази"] };
        var copy = FilmCopy(0, gems, "/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.1080p.mkv");
        var file = NewFile(0, "/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.2160p.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "473033")));

        //Act
        var result = ReplacementPairing.Pair([gems], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var byShownTitle = ReplacementPairing.Pair([shownOnly], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
        Assert.Equal("“/media/movies/Uncut Gems (2019)/Uncut.Gems.2019.2160p.mkv” claims Uncut, while the release claimed none.", TestTexts.En(byShownTitle.Reason));
    }

    [Fact]
    public void Pair_of_a_new_file_running_under_three_quarters_of_its_old_copy_stops_for_review_keeping_the_pair()
    {
        //Arrange
        // 1:56:30 old, 1:12:04 new: a featurette or another film under the work's ids (MAM-37's measure).
        var copy = FilmCopy(0, Alien, OldAlien, durationMilliseconds: 6_990_000);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")), durationMilliseconds: 4_324_000);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "“/media/movies/Alien (1979)/Alien.1979.2160p.mkv” runs 1:12:04, less than three quarters of the old copy's 1:56:30.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Theory]
    // Exactly three quarters is not under.
    [InlineData(3_000_000L, 4_000_000L, ReplacementPairingVerdict.Pass)]
    [InlineData(2_999_999L, 4_000_000L, ReplacementPairingVerdict.Review)]
    // A longer cut — Redux, Extended — passes: one direction only (Vasyl, 2026-10-10).
    [InlineData(9_000_000L, 4_000_000L, ReplacementPairingVerdict.Pass)]
    // An old copy never measured: nothing to compare.
    [InlineData(1_000L, null, ReplacementPairingVerdict.Pass)]
    public void Pair_compares_running_times_one_way_at_three_quarters(long newMilliseconds, long? oldMilliseconds, ReplacementPairingVerdict verdict)
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien, durationMilliseconds: oldMilliseconds);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")), durationMilliseconds: newMilliseconds);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(verdict, result.Verdict);
    }

    [Fact]
    public void Pair_says_the_edition_before_the_running_time()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien, durationMilliseconds: 6_990_000);
        var file = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.IMAX.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")), durationMilliseconds: 1_000);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal("“/media/movies/Alien (1979)/Alien.1979.IMAX.mkv” claims IMAX, while the release claimed none.", TestTexts.En(result.Reason));
    }
}
