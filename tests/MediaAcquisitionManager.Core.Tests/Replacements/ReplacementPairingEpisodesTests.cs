using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using static MediaAcquisitionManager.TestInfrastructure.Replacements.ReplacementPairingInputs;

namespace MediaAcquisitionManager.Core.Tests.Replacements;

// MAM-25 — check 3 and the pairs for episodes (ADR 0017, ADR 0032, ADR 0035): an episode's new item must be in the old
// copy's series by a registry id of the show, at the copy's season and episode number. A season pack pairs each ticked
// episode with its own file, and an episode it brings that no copy is ticked of is simply added. An old copy without
// numbers, one that is an a/b half, one covering several episodes — by the library's end number or its name — and one the
// library now lists as a film, places in another series or numbers otherwise than when it was scheduled stop the job; so
// do a new file covering several episodes or one that is a half, where it would replace a ticked copy, two new files of
// one episode, no new file of a ticked episode, another series — even once every ticked episode paired, unlike a
// collection's other film —, conflicting series ids, series ids for a show that has none and an episode item in a film
// job. No series id — an old copy's or a new file's —, series ids only of schemes the show has none of, or no numbers yet
// wait until the hour. An episode's own titles claim no edition of its new file.
public sealed class ReplacementPairingEpisodesTests
{
    private const string Season2 = "/media/tv-shows/The Penguins of Madagascar/Season 02";
    private const string Downloads = "/media/tv-shows/The Penguins of Madagascar/Season 02 [new]";

    [Fact]
    public void Pair_of_a_season_pack_pairs_each_ticked_episode_and_simply_adds_the_extra_one()
    {
        //Arrange
        var first = EpisodeCopy(0, 2, 1, $"{Season2}/Penguins.S02E01.mkv");
        var second = EpisodeCopy(1, 2, 2, $"{Season2}/Penguins.S02E02.mkv");
        var newFirst = NewFile(0, $"{Downloads}/Penguins.S02E01.1080p.mkv", EpisodeItem(2, 1));
        var newSecond = NewFile(1, $"{Downloads}/Penguins.S02E02.1080p.mkv", EpisodeItem(2, 2));
        var extra = NewFile(2, $"{Downloads}/Penguins.S02E03.1080p.mkv", EpisodeItem(2, 3));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [first, second], [newFirst, newSecond, extra], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal((ReplacementPairingVerdict.Pass, (string?)null), (result.Verdict, result.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(first.Id, newFirst.Id, null), new ReplacementOldCopyOutcome(second.Id, newSecond.Id, null)], result.OldCopies);
        Assert.Equal(
            [
                new ReplacementNewFileOutcome(newFirst.Id, Penguins.WorkId, null),
                new ReplacementNewFileOutcome(newSecond.Id, Penguins.WorkId, null),
                new ReplacementNewFileOutcome(extra.Id, Penguins.WorkId, null),
            ],
            result.NewFiles);
    }

    [Theory]
    [InlineData(MediaIdentifierScheme.Imdb, "tt0892700")]
    [InlineData(MediaIdentifierScheme.TmdbTv, "1044")]
    [InlineData(MediaIdentifierScheme.Tvdb, "80703")]
    public void Pair_of_an_episode_whose_series_carries_one_of_the_shows_ids_passes(MediaIdentifierScheme scheme, string value)
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: [Id(scheme, value)]));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Pair_of_an_episode_placed_in_another_series_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "76703")]));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” placed “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” in another series (TVDB 76703), not The Penguins of Madagascar."),
            (result.Verdict, result.Reason));
    }

    [Fact]
    public void Pair_of_a_season_pack_whose_extra_episode_is_of_another_series_stops_for_review_though_every_ticked_episode_paired()
    {
        //Arrange
        // Unlike a collection's other film, an episode of another series is never simply added (Vasyl, 2026-10-10).
        var copy = EpisodeCopy(0, 2, 1, $"{Season2}/Penguins.S02E01.mkv");
        var newFirst = NewFile(0, $"{Downloads}/Penguins.S02E01.1080p.mkv", EpisodeItem(2, 1));
        var another = NewFile(1, $"{Downloads}/Penguins.S02E02.1080p.mkv", EpisodeItem(2, 2, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "76703")]));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [newFirst, another], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Jellyfin” placed “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E02.1080p.mkv” in another series (TVDB 76703), not The Penguins of Madagascar.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, result.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, newFirst.Id, null)], result.OldCopies);
        Assert.Equal([new ReplacementNewFileOutcome(newFirst.Id, Penguins.WorkId, null), new ReplacementNewFileOutcome(another.Id, null, reason)], result.NewFiles);
    }

    [Fact]
    public void Pair_of_an_episode_whose_series_ids_conflict_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(
            0,
            $"{Downloads}/Penguins.S02E05.mkv",
            EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "80703"), Id(MediaIdentifierScheme.TmdbTv, "999")]));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“Jellyfin” gave the series of “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” conflicting ids: TVDB 80703 is The Penguins of Madagascar's, TMDb TV 999 is not.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_episode_whose_series_carries_only_a_film_scheme_has_no_id_yet()
    {
        //Arrange
        // ADR 0017: a show's TMDb id is TMDb TV's; a film's TMDb number names another thing.
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tmdb, "1044")]));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Wait, result.Verdict);
    }

    [Fact]
    public void Pair_of_an_episode_whose_series_ids_share_no_scheme_with_the_shows_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        // A TMDb TV id compares with nothing of a show known only by TVDB: no other show is proven, so it waits as a file
        // without an id does — the library may still give the series a TVDB id.
        var penguins = Penguins with { Identifiers = [Id(MediaIdentifierScheme.Tvdb, "80703")] };
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.TmdbTv, "1044")]));

        //Act
        var waiting = ReplacementPairing.Pair([penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        const string reason =
            "“Jellyfin” gave the series of “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” TMDb TV 1044 within an hour, but The Penguins of Madagascar is known here only by TVDB 80703.";
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (waiting.Verdict, waiting.Reason));
        Assert.Equal([new ReplacementNewFileOutcome(file.Id, null, null)], waiting.NewFiles);
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (stopped.Verdict, stopped.Reason));
        Assert.Equal([new ReplacementNewFileOutcome(file.Id, null, reason)], stopped.NewFiles);
    }

    [Fact]
    public void Pair_of_an_episode_of_a_show_with_no_id_stops_for_review_at_once()
    {
        //Arrange
        // A show confirmed by its title and year alone: no id the library gives could ever show an item to be its.
        var penguins = Penguins with { Identifiers = [] };
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "80703")]));

        //Act
        var result = ReplacementPairing.Pair([penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        // The old copy's own series says so first: the ticked copies' problems come before the new files'.
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” gave the series of the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” IMDb tt0892700, TVDB 80703 and TMDb TV 1044, but The Penguins of Madagascar has no TMDb, TVDB or IMDb id here to compare them with."),
            (result.Verdict, result.Reason));
        Assert.Equal(
            "“Jellyfin” gave the series of “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” TVDB 80703, but The Penguins of Madagascar has no TMDb, TVDB or IMDb id here to compare it with.",
            Assert.Single(result.NewFiles).Problem);
    }

    [Fact]
    public void Pair_of_an_episode_without_a_series_item_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5) with { SeriesItemId = null });

        //Act
        var waiting = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        // An episode without a series — the library's orphan episode item — can never pass.
        Assert.Equal(ReplacementPairingVerdict.Wait, waiting.Verdict);
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” gave “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of The Penguins of Madagascar."),
            (stopped.Verdict, stopped.Reason));
    }

    [Fact]
    public void Pair_of_an_episode_whose_series_has_no_ids_waits()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: []));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (result.Verdict, result.Reason));
    }

    [Fact]
    public void Pair_of_an_episode_without_a_season_number_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(null, 5));

        //Act
        var waiting = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Wait, waiting.Verdict);
        Assert.Equal(
            "“Jellyfin” gave “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” no season and episode number within an hour, so it can't be paired with an old copy.",
            stopped.Reason);
        // Identified, though: its series is the copy's.
        Assert.Equal(Penguins.WorkId, Assert.Single(stopped.NewFiles).WorkId);
    }

    [Fact]
    public void Pair_of_an_episode_item_in_a_film_job_stops_for_review()
    {
        //Arrange
        var copy = FilmCopy(0, Alien, "/media/movies/Alien (1979)/Alien.1979.mkv");
        var file = NewFile(0, "/media/movies/Alien (1979)/Alien.1979.2160p.mkv", EpisodeItem(1, 1));

        //Act
        var result = ReplacementPairing.Pair([Alien], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“Jellyfin” made “/media/movies/Alien (1979)/Alien.1979.2160p.mkv” an episode, while the old copies are of the film Alien (1979).",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_old_copy_without_an_episode_number_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, null, $"{Season2}/Penguins - Special.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“Jellyfin” gave the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins - Special.mkv” no season and episode number, so the app can't tell which new file replaces it.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_old_copy_that_is_an_a_or_b_half_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 3, 9, "/media/cartoons/Mickey/Season 03/Mickey Mouse Clubhouse_S03E09a_-_Road Rally.mkv");
        var file = NewFile(0, "/media/cartoons/Mickey/Season 03 [new]/Mickey.S03E09.mkv", EpisodeItem(3, 9));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "“/media/cartoons/Mickey/Season 03/Mickey Mouse Clubhouse_S03E09a_-_Road Rally.mkv” is one half of an episode (its name gives the episode the letter “a”), so it can't be replaced on its own.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, result.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, null, reason)], result.OldCopies);
    }

    [Fact]
    public void Pair_of_an_old_copy_the_library_ends_past_its_episode_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins - Double Episode.mkv", library: EpisodeItem(2, 5, end: 6));
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins - Double Episode.mkv” covers episodes 5–6 of season 2, so one new episode can't replace it.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_old_copy_whose_name_covers_several_episodes_stops_for_review_where_the_library_gives_no_end()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05-E06.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05-E06.mkv” covers episodes 5–6 of season 2, so one new episode can't replace it.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_old_copy_the_library_now_lists_as_a_film_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: FilmItem());
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“Jellyfin” lists the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” as a film, while its new file would be an episode, so the two can't be paired.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_old_copy_the_library_lists_as_neither_film_nor_episode_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: OtherItem());
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“Jellyfin” lists the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” as neither a film nor an episode, so no new episode can replace it.",
            result.Reason);
    }

    [Theory]
    // Another number in the same season, and the same number in another season.
    [InlineData(2, 6, "season 2, episode 6")]
    [InlineData(3, 5, "season 3, episode 5")]
    public void Pair_of_an_old_copy_the_library_now_numbers_otherwise_stops_for_review_and_pairs_nothing(int season, int episode, string now)
    {
        //Arrange
        // The owner renumbered the old item after it was scheduled: the library's word now decides (ADR 0017), so its E05
        // is no longer what the new E05 would replace.
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: EpisodeItem(season, episode));
        var newFifth = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));
        var newSixth = NewFile(1, $"{Downloads}/Penguins.S02E06.mkv", EpisodeItem(2, 6));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [newFifth, newSixth], [], "Living room", libraryWaitPassed: false);

        //Assert
        var reason =
            $"“Living room” now numbers the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” {now}, not season 2, episode 5 as when it was scheduled.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, result.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, null, reason)], result.OldCopies);
    }

    [Fact]
    public void Pair_of_an_old_copy_the_library_now_gives_no_season_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: EpisodeItem(null, 5));
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” now gives the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” no season and episode number, not season 2, episode 5 as when it was scheduled."),
            (result.Verdict, result.Reason));
    }

    [Fact]
    public void Pair_of_an_old_copy_the_library_now_places_in_another_series_stops_for_review()
    {
        //Arrange
        // The owner re-identified the old item: the library's word now decides, whatever its series was when it was ticked.
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "999")]));
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], "Living room", libraryWaitPassed: false);

        //Assert
        const string reason =
            "“Living room” placed the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” in another series (TVDB 999), not The Penguins of Madagascar.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, result.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, null, reason)], result.OldCopies);
    }

    [Fact]
    public void Pair_of_an_old_copy_whose_series_ids_conflict_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(
            0,
            2,
            5,
            $"{Season2}/Penguins.S02E05.mkv",
            library: EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "80703"), Id(MediaIdentifierScheme.TmdbTv, "999")]));
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“Jellyfin” gave the series of the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” conflicting ids: TVDB 80703 is The Penguins of Madagascar's, TMDb TV 999 is not.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_an_old_copy_whose_series_has_no_id_waits_keeping_its_pair_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: EpisodeItem(2, 5) with { SeriesItemId = null });
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5));

        //Act
        var waiting = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        // A missing id waits as a new file's does: the library or the owner may still give the series one.
        const string reason =
            "“Jellyfin” gave the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” no series with a TMDb, TVDB or IMDb id within an hour, so it can't be told to be an episode of The Penguins of Madagascar.";
        Assert.Equal((ReplacementPairingVerdict.Wait, (string?)null), (waiting.Verdict, waiting.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, file.Id, null)], waiting.OldCopies);
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (stopped.Verdict, stopped.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, file.Id, reason)], stopped.OldCopies);
        Assert.Equal([new ReplacementNewFileOutcome(file.Id, Penguins.WorkId, null)], stopped.NewFiles);
    }

    [Fact]
    public void Pair_of_an_old_copy_whose_series_ids_share_no_scheme_with_the_shows_waits_and_stops_once_the_hour_ran_out()
    {
        //Arrange
        var penguins = Penguins with { Identifiers = [Id(MediaIdentifierScheme.Tvdb, "80703")] };
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", library: EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Imdb, "tt0892700")]));
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, seriesIdentifiers: [Id(MediaIdentifierScheme.Tvdb, "80703")]));

        //Act
        var waiting = ReplacementPairing.Pair([penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);
        var stopped = ReplacementPairing.Pair([penguins], [copy], [file], [], LibraryName, libraryWaitPassed: true);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Wait, waiting.Verdict);
        Assert.Equal(
            (ReplacementPairingVerdict.Review,
                "“Jellyfin” gave the series of the old copy “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” IMDb tt0892700 within an hour, but The Penguins of Madagascar is known here only by TVDB 80703."),
            (stopped.Verdict, stopped.Reason));
    }

    [Fact]
    public void Pair_of_a_new_file_the_library_ends_past_a_ticked_episode_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 6, $"{Season2}/Penguins.S02E06.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.mkv", EpisodeItem(2, 5, end: 6));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.mkv” covers episodes 5–6 of season 2, so it can't replace “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E06.mkv” alone.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_a_new_file_whose_name_covers_a_ticked_episode_stops_for_review_where_the_library_gives_no_end()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05E06.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05E06.mkv” covers episodes 5–6 of season 2, so it can't replace “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E05.mkv” alone.",
            result.Reason);
    }

    [Fact]
    public void Pair_widens_a_new_files_episodes_by_its_name_and_never_narrows_them()
    {
        //Arrange
        // The library places the file at 5; its name says 6 to 7. It is taken for 5 to 7, so the ticked 7 stops.
        var copy = EpisodeCopy(0, 2, 7, $"{Season2}/Penguins.S02E07.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E06-E07.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E06-E07.mkv” covers episodes 5–7 of season 2, so it can't replace “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E07.mkv” alone.",
            result.Reason);
    }

    [Fact]
    public void Pair_simply_adds_a_new_file_of_several_episodes_no_ticked_copy_is_among()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 1, $"{Season2}/Penguins.S02E01.mkv");
        var newFirst = NewFile(0, $"{Downloads}/Penguins.S02E01.mkv", EpisodeItem(2, 1));
        var twoEpisodes = NewFile(1, $"{Downloads}/Penguins.S02E05-E06.mkv", EpisodeItem(2, 5, end: 6));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [newFirst, twoEpisodes], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, newFirst.Id, null)], result.OldCopies);
    }

    [Fact]
    public void Pair_of_a_new_file_that_is_a_half_of_a_ticked_episode_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 3, 9, "/media/cartoons/Mickey/Season 03/Mickey.S03E09.mkv");
        var file = NewFile(0, "/media/cartoons/Mickey/Season 03 [new]/Mickey Mouse Clubhouse_S03E09b_-_Road Rally.mkv", EpisodeItem(3, 9));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "“/media/cartoons/Mickey/Season 03 [new]/Mickey Mouse Clubhouse_S03E09b_-_Road Rally.mkv” is one half of an episode (its name gives the episode the letter “b”), so it can't replace “/media/cartoons/Mickey/Season 03/Mickey.S03E09.mkv” on its own.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_two_new_files_of_one_ticked_episode_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var first = NewFile(0, $"{Downloads}/Penguins.S02E05.1080p.mkv", EpisodeItem(2, 5));
        var second = NewFile(1, $"{Downloads}/Penguins.S02E05.720p.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [first, second], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(
            "Two new files are The Penguins of Madagascar, season 2, episode 5: “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.1080p.mkv” and “/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.720p.mkv”; the app can't tell which one replaces the old copy.",
            result.Reason);
    }

    [Fact]
    public void Pair_of_a_ticked_episode_no_new_file_is_stops_for_review()
    {
        //Arrange
        var copy = EpisodeCopy(0, 2, 7, $"{Season2}/Penguins.S02E07.mkv");
        // The same number in another season is another episode.
        var file = NewFile(0, $"{Downloads}/Penguins.S03E07.mkv", EpisodeItem(3, 7));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        const string reason =
            "No new file is The Penguins of Madagascar, season 2, episode 7, so “/media/tv-shows/The Penguins of Madagascar/Season 02/Penguins.S02E07.mkv” has nothing to replace it.";
        Assert.Equal((ReplacementPairingVerdict.Review, reason), (result.Verdict, result.Reason));
        Assert.Equal([new ReplacementOldCopyOutcome(copy.Id, null, reason)], result.OldCopies);
    }

    [Fact]
    public void Pair_reads_no_edition_from_the_episodes_own_title()
    {
        //Arrange
        // MAM-37's rule: an episode's own titles are no edition of its file, as its show's aren't.
        var titled = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv", episodeTitles: ["Остаточна версія", "The Final Cut"]);
        var untitled = EpisodeCopy(0, 2, 5, $"{Season2}/Penguins.S02E05.mkv");
        var file = NewFile(0, $"{Downloads}/Penguins.S02E05.The.Final.Cut.1080p.mkv", EpisodeItem(2, 5));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [titled], [file], [], LibraryName, libraryWaitPassed: false);
        var withoutTitles = ReplacementPairing.Pair([Penguins], [untitled], [file], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.Equal(ReplacementPairingVerdict.Pass, result.Verdict);
        Assert.Equal(
            "“/media/tv-shows/The Penguins of Madagascar/Season 02 [new]/Penguins.S02E05.The.Final.Cut.1080p.mkv” claims Final cut, while the release claimed none.",
            withoutTitles.Reason);
    }

    [Fact]
    public void Pair_says_an_old_copys_own_problem_before_its_new_files()
    {
        //Arrange
        // The copy is a half and two new files claim it: the copy's own word comes first.
        var copy = EpisodeCopy(0, 3, 9, "/media/cartoons/Mickey/Season 03/Mickey_S03E09a.mkv");
        var first = NewFile(0, "/media/cartoons/Mickey/new/Mickey.S03E09.1080p.mkv", EpisodeItem(3, 9));
        var second = NewFile(1, "/media/cartoons/Mickey/new/Mickey.S03E09.720p.mkv", EpisodeItem(3, 9));

        //Act
        var result = ReplacementPairing.Pair([Penguins], [copy], [first, second], [], LibraryName, libraryWaitPassed: false);

        //Assert
        Assert.StartsWith("“/media/cartoons/Mickey/Season 03/Mickey_S03E09a.mkv” is one half of an episode", result.Reason, StringComparison.Ordinal);
    }
}
