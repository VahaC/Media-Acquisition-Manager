using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using static MediaAcquisitionManager.TestInfrastructure.Replacements.ReplacementPairingInputs;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-27 (Vasyl, 2026-10-11), ADR 0032 — which of a ticked copy's problems only dropping the copy, or keeping both copies,
// settles: the owner's Retry is withheld while a copy left in the job has one, as no fix in the library or the download
// client changes it and a new check would only stop again. The library numbering the old episode otherwise — or giving it
// no number now —, no new episode at a ticked one's place, an old copy or its new file that is one half of an episode or
// covers several, and a pair whose name claims a cut the release didn't or that runs under three quarters of the old copy
// are marked so. A film with no new file, two new files of a copy, an old item the library gives no number, lists as a
// film or as no episode, or places in another series, a sentence of what was still awaited once the hour ran out, and a
// paired copy are not. The mark goes with each copy's own problem alone, whatever the verdict.
public sealed class ReplacementPairingDropOnlyTests
{
    private const string Season2 = "/media/tv-shows/The Penguins of Madagascar/Season 02";
    private const string Downloads = "/media/tv-shows/The Penguins of Madagascar/Season 02 [new]";
    private const string OldEpisode = "/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv";
    private const string NewEpisode = "/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv";
    private const string OldAlien = "/media/movies/Alien (1979)/Alien.1979.1080p.mkv";
    private const string NewAlien = "/media/movies/Alien (1979)/Alien.1979.2160p.mkv";

    // ── only a drop settles ─────────────────────────────────────────────────────────────────────

    [Theory]
    // Another number in the season, and none at all: the copy was ticked for the place it had.
    [InlineData(2, 6, "numbers the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” season 2, episode 6")]
    [InlineData(null, 5, "gives the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” no season and episode number")]
    public void Pair_marks_an_old_copy_the_library_now_numbers_otherwise_as_only_a_drop_settling_it(int? season, int? episode, string now)
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(season, episode));
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        var reason = $"“Jellyfin” now {now}, not season 2, episode 5 as when it was scheduled.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_a_ticked_episode_no_new_file_is_as_only_a_drop_settling_it()
    {
        //Arrange
        // Every new file identified and numbered, none at the ticked place: the library placed it elsewhere, or the
        // download holds no such episode.
        var copy = EpisodeCopy(0, 2, 7, $"{Season2}/Penguins.S02E07.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E08.mkv", EpisodeItem(2, 8));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "No new file is The Penguins of Madagascar, season 2, episode 7, so “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E07.mkv” has nothing to replace it.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_an_old_copy_that_is_one_half_of_an_episode_as_only_a_drop_settling_it()
    {
        //Arrange
        var copy = EpisodeCopy(0, 3, 9, "/media/cartoons/Mickey/Season 03/Mickey Mouse Clubhouse_S03E09a_-_Road Rally.mkv");
        var file = NewFile(0, "/media/cartoons/Mickey/Season 03 [new]/Mickey.S03E09.mkv", EpisodeItem(3, 9));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“/media/cartoons/Mickey/Season 03/Mickey Mouse Clubhouse_S03E09a_-_Road Rally.mkv” is one half of an episode (its name gives the episode the letter “a”), so it can't be replaced on its own.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Theory]
    // By its name, where the library gives no end, and by the library's end number.
    [InlineData("Penguins.S02E05-E06.mkv", null)]
    [InlineData("Penguins - Double Episode.mkv", 6)]
    public void Pair_marks_an_old_copy_covering_several_episodes_as_only_a_drop_settling_it(string name, int? end)
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/{name}", library: EpisodeItem(2, 5, end: end));
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        var reason = $"“/media/tv-shows/The Penguins of Madagascar/Season 02/{name}” covers episodes 5–6 of season 2, so one new episode can't replace it.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Theory]
    // By the library's end number, and by its name where the library gives no end.
    [InlineData("Penguins.S02E05.mkv", 6)]
    [InlineData("Penguins.S02E05E06.mkv", null)]
    public void Pair_marks_a_ticked_episode_whose_new_file_covers_the_next_too_as_only_a_drop_settling_it(string name, int? end)
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 6, $"{Season2}/Penguins.S02E06.mkv");
        var file = NewFile(0, $"{Downloads}/{name}", EpisodeItem(2, 5, end: end));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        var reason =
            $"“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/{name}” covers episodes 5–6 of season 2, so it can't replace “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E06.mkv” alone.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_a_ticked_episode_whose_new_file_is_one_half_of_it_as_only_a_drop_settling_it()
    {
        //Arrange
        var copy = EpisodeCopy(0, 3, 9, "/media/cartoons/Mickey/Season 03/Mickey.S03E09.mkv");
        var file = NewFile(0, "/media/cartoons/Mickey/Season 03 [new]/Mickey Mouse Clubhouse_S03E09b_-_Road Rally.mkv", EpisodeItem(3, 9));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“/media/cartoons/Mickey/Season 03 [new]/Mickey Mouse Clubhouse_S03E09b_-_Road Rally.mkv” is one half of an episode (its name gives the episode the letter “b”), so it can't replace “/media/cartoons/Mickey/Season 03/Mickey.S03E09.mkv” on its own.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_a_pair_whose_name_claims_an_edition_the_release_did_not_as_only_a_drop_settling_it()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var file = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.Extended.2160p.mkv", FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "“/media/movies/Alien (1979)/Alien.1979.Extended.2160p.mkv” claims Extended cut, while the release claimed none.";
        // The pair stays shown beside its mark: the owner sees which file it was.
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_a_pair_running_under_three_quarters_of_its_old_copy_as_only_a_drop_settling_it()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien, durationMilliseconds: 6_990_000);
        var file = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")), durationMilliseconds: 4_324_000);

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "“/media/movies/Alien (1979)/Alien.1979.2160p.mkv” runs 1:12:04, less than three quarters of the old copy's 1:56:30.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_a_problem_only_a_drop_settles_while_the_job_still_waits()
    {
        //Arrange
        // The new file isn't listed yet, so the job waits; the copy's own problem is shown, and marked, all the same.
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(2, 6));
        var file = NewFile(0, NewEpisode, ReplacementLibraryReading.NotListed);

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” now numbers the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” season 2, episode 6, not season 2, episode 5 as when it was scheduled.";
        Assert.Equal(ReplacementPairingVerdict.Wait, result.Verdict);
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: true)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_marks_each_ticked_copy_by_its_own_problem()
    {
        //Arrange
        // A season pack: episode 1 pairs, episode 5 has no new file, episode 7 two.
        var first = EpisodeCopy(0, 2, 1, $"{Season2}/Penguins.S02E01.mkv");
        var fifth = EpisodeCopy(1, 2, 5, OldEpisode);
        var seventh = EpisodeCopy(2, 2, 7, $"{Season2}/Penguins.S02E07.mkv");
        var newFirst = NewFile(0, $"{Downloads}/Penguins.S02E01.mkv", EpisodeItem(2, 1));
        var newSeventh = NewFile(1, $"{Downloads}/Penguins.S02E07.1080p.mkv", EpisodeItem(2, 7));
        var newSeventhHd = NewFile(2, $"{Downloads}/Penguins.S02E07.720p.mkv", EpisodeItem(2, 7));

        //Act
        var result = ReplacementPairing.Pair(
            [Penguins], [seventh, first, fifth], [newFirst, newSeventh, newSeventhHd], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string noNewFile =
            "No new file is The Penguins of Madagascar, season 2, episode 5, so “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” has nothing to replace it.";
        const string twoNewFiles =
            "Two new files are The Penguins of Madagascar, season 2, episode 7: “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E07.1080p.mkv” and "
            + "“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E07.720p.mkv”; the app can't tell which one replaces the old copy.";
        Assert.Equal((ReplacementPairingVerdict.Review, noNewFile), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal(
            [
                new EnglishOldCopyOutcome(first.Id, newFirst.Id, null),
                new EnglishOldCopyOutcome(fifth.Id, null, noNewFile, OnlyDropSettles: true),
                new EnglishOldCopyOutcome(seventh.Id, null, twoNewFiles),
            ],
            result.OldCopies.InEnglish());
    }

    // ── a retry may settle ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Pair_leaves_a_ticked_film_no_new_file_is_unmarked()
    {
        //Arrange
        // The film's new file may be one the library took for an extra: fixing its item there may still pair it.
        var copy = FilmCopy(0, Alien, OldAlien);
        var extra = NewFile(0, "/media/movies/Alien (1979)/Extras/Making of.mkv", OtherItem());

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [extra], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason = "No new file is Alien (1979), so “/media/movies/Alien (1979)/Alien.1979.1080p.mkv” has nothing to replace it.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_two_new_files_of_a_ticked_film_unmarked()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, OldAlien);
        var first = NewFile(0, NewAlien, FilmItem(Id(MediaIdentifierScheme.Tmdb, "348")));
        var second = NewFile(1, "/media/movies/Alien (1979)/Alien.1979.1080p.REMUX.mkv", FilmItem(Id(MediaIdentifierScheme.Imdb, "tt0078748")));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [first, second], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "Two new files are Alien (1979): “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” and “/media/movies/Alien (1979)/Alien.1979.1080p.REMUX.mkv”; the app can't tell which one replaces the old copy.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_an_old_copy_scheduled_without_numbers_unmarked()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, null, $"{Season2}/Penguins - Special.mkv");
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” gave the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins - Special.mkv” no season and episode number, so the app can't tell which new file replaces it.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Theory]
    [InlineData(true, "as a film, while its new file would be an episode, so the two can't be paired.")]
    [InlineData(false, "as neither a film nor an episode, so no new episode can replace it.")]
    public void Pair_leaves_an_old_copy_the_library_lists_as_no_episode_unmarked(bool asFilm, string why)
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: asFilm ? FilmItem() : OtherItem());
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        var reason = $"“Jellyfin” lists the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” {why}";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_an_old_copy_the_library_places_in_another_series_unmarked()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "999")]));
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” placed the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” in another series (TVDB 999), not The Penguins of Madagascar.";
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_an_old_copy_the_library_no_longer_listed_when_the_hour_ran_out_unmarked()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: ReplacementLibraryReading.NotListed);
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Jellyfin” no longer lists the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv”, so its episode numbers can't be read.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_an_old_copy_whose_series_had_no_id_when_the_hour_ran_out_unmarked()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode, library: EpisodeItem(2, 5, seriesIdentifiers: []));
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Jellyfin” gave the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of The Penguins of Madagascar.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, TestTexts.En(result.Reason)));
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, reason, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_a_ticked_episode_waiting_on_a_new_file_not_identified_yet_unmarked()
    {
        //Arrange
        // No new file at its place yet, while one has no series id: that file is the reason, never the copy.
        var copy = EpisodeCopy(0, 2, 5, OldEpisode);
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5) with { SeriesItemId = null });

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Review, result.Verdict);
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, null, null, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }

    [Fact]
    public void Pair_leaves_a_paired_copy_unmarked()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, OldEpisode);
        var file = NewFile(0, NewEpisode, EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
        Assert.Equal([new EnglishOldCopyOutcome(copy.Id, file.Id, null, OnlyDropSettles: false)], result.OldCopies.InEnglish());
    }
}
