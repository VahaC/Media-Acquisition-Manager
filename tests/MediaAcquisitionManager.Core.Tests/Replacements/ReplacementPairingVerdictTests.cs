using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Replacements.ReplacementPairingInputs;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — the verdict's order (Vasyl, 2026-10-10; ADR 0035): a file the library doesn't list or hasn't finished reading,
// a new one or a ticked episode's, waits — and once the hour ran out stops the job with the first such sentence —; then a
// definitive problem stops it at once, the ticked copies' by their place before the new files'; then a file not identified
// yet, a ticked episode's series or a new file, waits as the first did, the copies' again first; else the job is verified.
// While the library hasn't read everything, a wrong id doesn't stop it yet; a missing id never holds up a definitive
// problem. The outcomes come by place, whatever order they were given in.
public sealed class ReplacementPairingVerdictTests
{
    private const string OldAlien = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
    private const string NewAlien = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";
    private const string NewAliens = "/media/movies/Aliens (1986)/Aliens.1986.2160p.mkv";
    private const string OldEpisode = "/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv";
    private const string NewEpisode = "/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv";

    [Fact]
    public void Pair_of_a_new_file_the_library_does_not_list_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, ReplacementLibraryReading.NotListed);

        //Act
        var waiting = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        // An unavailable library never leads to a pass; the copy left without a file is no problem of its own meanwhile.
        const string reason = "“Jellyfin” didn't list “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” within an hour of being asked to look.";
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (waiting.Verdict, TestTexts.En(waiting.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, null)], waiting.NewFiles.InEnglish());
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (stopped.Verdict, TestTexts.En(stopped.Reason)));
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, reason)], stopped.NewFiles.InEnglish());
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null)], stopped.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_a_new_file_the_library_has_not_finished_reading_waits_whatever_ids_it_carries()
    {
        //Arrange
        // Jellyfin saves a new item before its refresh: what it carries before then counts for nothing.
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605")) with { Refreshed = false });

        //Act
        var waiting = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        Assert.Equal((ReplacementPairingVerdict.Wait, (Guid?)null), (waiting.Verdict, Assert.Single(waiting.NewFiles).WorkId));
        Assert.Equal(
            (ReplacementPairingVerdict.Review, "“Jellyfin” hadn't finished reading “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” within an hour."),
            (stopped.Verdict, TestTexts.En(stopped.Reason)));
    }

    [Fact]
    public void Pair_of_an_episode_copy_the_library_no_longer_lists_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        // The library may make an old item anew for a moment: a wait, never a problem of its own.
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: ReplacementLibraryReading.NotListed);
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var waiting = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Jellyfin” no longer lists the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv”, so its episode numbers can't be read.";
        Assert.Equal(ReplacementPairingVerdict.Wait, waiting.Verdict);
        // Its pair is found meanwhile, and shown.
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, null)], waiting.OldCopies.InEnglish());
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (stopped.Verdict, TestTexts.En(stopped.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason)], stopped.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_of_an_episode_copy_the_library_has_not_finished_reading_waits()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(2, 5) with { Refreshed = false });
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var waiting = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Wait, waiting.Verdict);
        Assert.Equal(
            "“Jellyfin” hadn't finished reading the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” within an hour, so its episode numbers can't be read.",
            TestTexts.En(stopped.Reason));
    }

    [Fact]
    public void Pair_of_a_film_copy_reads_no_old_item()
    {
        //Arrange
        // A film's old copy is never read from the library: its item says nothing a pair needs.
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Null(copy.Library);
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_of_a_wrong_id_waits_while_another_file_is_unread_and_stops_with_the_unread_one_once_the_hour_ran_out()
    {
        //Arrange
        var alienCopy = FilmCopy(0, Alien, OldAlien);
        var wrong = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0090605"), Id(MediaIdentifierScheme.Tmdb, "679")));
        var unread = NewFile(1, NewAliens, ReplacementLibraryReading.NotListed);

        //Act
        var waiting = ReplacementPairing.Pair([Alien], [alienCopy], [wrong, unread], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Alien], [alienCopy], [wrong, unread], [], LibraryName, libraryWaitPassed: true);

        //Assert
        // Vasyl, 2026-10-10: a wrong id stops at once only when every new file is listed and read; its row shows it meanwhile.
        const string wrongReason =
            "“Jellyfin” identified “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” as another work (IMDb tt0090605, TMDb 679), not Alien (1979).";
        const string unreadReason = "“Jellyfin” didn't list “/media/movies/Aliens (1986)/Aliens.1986.2160p.mkv” within an hour of being asked to look.";
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (waiting.Verdict, TestTexts.En(waiting.Reason)));
        Assert.Equal([wrongReason, null], waiting.NewFiles.Select(outcome => TestTexts.En(outcome.Problem)));
        Assert.Equal((ReplacementPairingVerdict.Review, unreadReason), (stopped.Verdict, TestTexts.En(stopped.Reason)));
        Assert.Equal([wrongReason, unreadReason], stopped.NewFiles.Select(outcome => TestTexts.En(outcome.Problem)));
    }

    [Fact]
    public void Pair_of_a_definitive_problem_stops_at_once_while_another_file_has_no_id_yet()
    {
        //Arrange
        var alienCopy = FilmCopy(0, Alien, OldAlien);
        var aliensCopy = FilmCopy(1, Aliens, "/media/movies/Aliens (1986)/Aliens.1986.1080p.mkv");
        var extended = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.Extended.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var withoutId = NewFile(1, NewAliens, FilmItem());

        //Act
        var result = ReplacementPairing.Pair([Alien, Aliens], [alienCopy, aliensCopy], [extended, withoutId], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review, "“/media/movies/Alien (1979)/Alien.1979.Extended.mkv” claims Extended cut, while the release claimed none."),
            (result.Verdict, TestTexts.En(result.Reason)));
        // The copy without a file yet waits on the file without an id: no problem of its own.
        Assert.Equal([new EnglishOldCopyOutcome(aliensCopy.Id, null, null)], result.OldCopies.InEnglish().Where(outcome => outcome.OldCopyId == aliensCopy.Id));
    }

    [Fact]
    public void Pair_of_a_definitive_problem_stops_at_once_while_an_old_copys_series_has_no_id_yet()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(2, 5) with { SeriesItemId = null });
        var extended = NewFile(0, "/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.Extended.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [extended], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.Extended.mkv” claims Extended cut, while the release claimed none.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, extended.Id, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_gives_an_old_copys_missing_series_id_before_a_new_files_once_the_hour_ran_out()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(2, 5, seriesIdentifiers: []));
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5) with { SeriesItemId = null });

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string copyReason =
            "“Jellyfin” gave the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of The Penguins of Madagascar.";
        const string fileReason =
            "“Jellyfin” gave “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of The Penguins of Madagascar.";
        Assert.Equal((ReplacementPairingVerdict.Review, copyReason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, copyReason)], result.OldCopies.InEnglish());
        Assert.Equal([new EnglishNewFileOutcome(file.Id, null, fileReason)], result.NewFiles.InEnglish());
    }

    [Fact]
    public void Pair_gives_the_ticked_copies_problems_by_their_place_before_the_new_files()
    {
        //Arrange
        var first = EpisodeCopy(0, 2, 5, "/media/tv-shows/Penguins/Season 02/Penguins.S02E05a.mkv");
        var second = EpisodeCopy(1, 2, 6, "/media/tv-shows/Penguins/Season 02/Penguins.S02E06b.mkv");
        var another = NewFile(0, NewEpisode, EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "1")]));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [second, first], [another], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/tv-shows/Penguins/Season 02/Penguins.S02E05a.mkv” is one half of an episode (its name gives the episode the letter “a”), so it can't be replaced on its own.",
            TestTexts.En(result.Reason));
        Assert.Equal([first.Id, second.Id], result.OldCopies.Select(outcome => outcome.OldCopyId));
        Assert.All([.. result.OldCopies.Select(outcome => outcome.Problem), Assert.Single(result.NewFiles).Problem], problem => Assert.NotNull(problem));
    }

    [Fact]
    public void Pair_gives_the_new_files_outcomes_by_their_place_in_the_clients_list()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode);
        var later = NewFile(4, "/media/tv-shows/Penguins/new/Penguins.S02E07.mkv", EpisodeItem(2, 7));
        var earlier = NewFile(1, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [later, earlier], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal([earlier.Id, later.Id], result.NewFiles.Select(outcome => outcome.NewFileId));
    }

    [Fact]
    public void Pair_of_a_ticked_copy_without_a_new_file_while_a_file_is_another_works_stops_with_that_files_reason()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var wrong = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "679")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [wrong], [], LibraryName, libraryWaitPassed: false);

        //Assert
        // The owner fixing that file's item may pair the copy: the file is the reason, not "no new file".
        Assert.Equal(
            "“Jellyfin” identified “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” as another work (TMDb 679), not Alien (1979).", TestTexts.En(result.Reason));
        Assert.Null(Assert.Single(result.OldCopies).Problem);
    }

    [Fact]
    public void Pair_of_a_verified_job_carries_no_problem_on_any_row()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien, durationMilliseconds: 6_990_000);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")), durationMilliseconds: 8_000_000);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [MediaEdition.Extended], LibraryName, libraryWaitPassed: true);

        //Assert
        // Once the hour ran out a job with nothing missing still passes.
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
        Assert.All(result.NewFiles, outcome => Assert.Null(outcome.Problem));
        Assert.All(result.OldCopies, outcome => Assert.Null(outcome.Problem));
    }

    [Fact]
    public void Pair_names_the_library_by_its_integrations_name_in_quotes()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, NewAlien, ReplacementLibraryReading.NotListed);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], "Living room", libraryWaitPassed: true);

        //Assert
        Assert.StartsWith("“Living room” didn't list", TestTexts.En(result.Reason), StringComparison.Ordinal);
    }

    [Fact]
    public void Pair_of_a_copy_of_a_work_the_job_did_not_match_throws()
    {
        //Arrange
        var copy = FilmCopy(0, Aliens, OldAlien);

        //Act
        var exception = Record.Exception(() => ReplacementPairing.Pair([Alien], [copy], [], [], LibraryName, libraryWaitPassed: false));

        //Assert
        // The job's rows are written together: a caller's mistake, not an outcome.
        Assert.IsType<ArgumentException>(exception);
    }
}
