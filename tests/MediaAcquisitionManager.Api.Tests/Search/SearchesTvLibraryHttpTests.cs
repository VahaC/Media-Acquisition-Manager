using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-32, ADR 0019 — "In your library" for a show: a search finds movies, series and episodes of no
// series, as the Library page lists them. A series is one card with all its seasons, found by its own title,
// year or id, or by an episode's title, id or file path — which counts for the series, once, with the
// episode's title or id as the evidence shown — and never lists its episodes. A series holding no episode is
// not listed. The works listed by their words are counted as cards: a series once, however many of its
// episodes hold the words. No release source is set up here.
public sealed class SearchesTvLibraryHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string FilmItemId = "1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f1f";
    private const string ShePlaysItemId = "2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f2f";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;
    private IntegrationConnectionEntity _livingRoom = null!;

    public SearchesTvLibraryHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A search by a word of an episode's title or of its file's path alone, and the episode's
    /// title shown as the one holding it; <c>null</c> for a path.</summary>
    public static TheoryData<string, string?> EpisodeWords => new()
    {
        { "thundering", "A Thundering" },
        { "S02E01", null },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        _jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        _jellyfin.Movies.Clear();
        // A film of the show's title, and one holding the word.
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(FilmItemId, "Yellowstone", null, 2009, "/media/documentary/Yellowstone (2009).mkv", "tt1426371"));
        _jellyfin.Movies.Add(JellyfinSamples.MovieWith(ShePlaysItemId, "Wild Yellowstone She Wolf", null, 2013, "/media/documentary/She Wolf (2013).mkv"));
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('a'), "Yellowstone", 2018, "/media/tv-shows/Yellowstone", "tt4236770", "73586", "341164"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('b'), "Daybreak", ItemId('a'), 1, 1, "/media/tv-shows/Yellowstone/Season 01/Yellowstone S01E01.mkv", "tt7529124", "6507316"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('c'), "Kill the Messenger", ItemId('a'), 1, 2, "/media/tv-shows/Yellowstone/Season 01/Yellowstone S01E02.mkv", "tt7529126"));
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
            ItemId('d'), "A Thundering", ItemId('a'), 2, 1, "/media/tv-shows/Yellowstone/Season 02/Yellowstone S02E01.mkv"));
        // A series every episode of which is missing: none stored.
        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('e'), "Clarkson's Farm", 2021, "/media/tv-shows/Clarksons Farm", "tt10541088", "117648", "378165"));
        // The Tom & Jerry Kids item: an episode of no series.
        _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(ItemId('f'), "Flippin Fido", null, 1, 4, "/media/cartoons/Tom & Jerry Kids/1.mkv"));
        _jellyfin.Arm(_factory.JellyfinHttp);
        _livingRoom = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_lists_a_series_found_by_its_title_and_year_once_with_every_season_beside_the_movies()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var search = await CreateSearchAsync(new SearchRequest("Yellowstone", "2018"), cancellationToken);

        //Assert
        // The series by its own title and year, never its episodes, whose paths hold the title; the film of
        // the title by the title alone; and the film holding the word.
        Assert.Equal(
            ["Series Yellowstone TitleAndYear", "Movie Yellowstone TitleOnly", "Movie Wild Yellowstone She Wolf Words"],
            search.Library.Select(match => $"{match.Work.Kind} {match.Work.Title} {match.Evidence}"));
        Assert.Equal(0, search.LibraryUnlistedCount);
        var series = search.Library[0];
        Assert.Equal(new LibraryTitleDto("Yellowstone", MediaTitleKind.Display, 2018, "Living room"), series.MatchedTitle);
        Assert.Empty(series.MatchedIdentifiers);
        // The Library page's card: all its seasons, no versions of its own.
        Assert.Equal([new LibrarySeasonDto(1, 2), new LibrarySeasonDto(2, 1)], series.Work.Seasons);
        Assert.Null(series.Work.Episode);
        Assert.Empty(series.Work.Versions);
        Assert.Equal(
            [MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tvdb, MediaIdentifierScheme.TmdbTv],
            series.Work.Identifiers.Select(identifier => identifier.Scheme).Order());
    }

    [Fact]
    public async Task Create_by_an_episodes_imdb_id_lists_its_series_with_the_episodes_id_as_the_evidence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // A title no work has: only the episode's id can find anything.
        var search = await CreateSearchAsync(new SearchRequest("Zzz", ImdbId: "TT7529126"), cancellationToken);

        //Assert
        var match = Assert.Single(search.Library);
        Assert.Equal((MediaWorkKind.Series, "Yellowstone", MatchEvidence.Identifier), (match.Work.Kind, match.Work.Title, match.Evidence));
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt7529126", "Living room")], match.MatchedIdentifiers);
        Assert.Null(match.MatchedTitle);
        // The episode's evidence, which its own id doesn't contradict: the series' other IMDb id is no conflict.
        Assert.Empty(match.ConflictingSchemes);
        Assert.Equal([new LibrarySeasonDto(1, 2), new LibrarySeasonDto(2, 1)], match.Work.Seasons);
    }

    [Fact]
    public async Task Create_by_an_episodes_title_lists_its_series_with_that_title_as_the_evidence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var search = await CreateSearchAsync(new SearchRequest("kill the messenger"), cancellationToken);

        //Assert
        var match = Assert.Single(search.Library);
        Assert.Equal((MediaWorkKind.Series, "Yellowstone", MatchEvidence.TitleOnly), (match.Work.Kind, match.Work.Title, match.Evidence));
        Assert.Equal("Kill the Messenger", match.MatchedTitle!.Text);
    }

    [Theory]
    [MemberData(nameof(EpisodeWords))]
    public async Task Create_by_a_word_of_an_episodes_title_or_path_lists_its_series_by_its_words(string text, string? holdingTitle)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var search = await CreateSearchAsync(new SearchRequest(text), cancellationToken);

        //Assert
        var match = Assert.Single(search.Library);
        Assert.Equal((MediaWorkKind.Series, "Yellowstone", MatchEvidence.Words), (match.Work.Kind, match.Work.Title, match.Evidence));
        Assert.Equal(holdingTitle, match.MatchedTitle?.Text);
    }

    [Fact]
    public async Task Create_by_the_series_id_lists_the_series_once_on_its_own_id_over_its_episodes_words()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        // The series' own id and title match, and every episode's path holds the title: the series once, on
        // its own, stronger evidence.
        var search = await CreateSearchAsync(new SearchRequest("Yellowstone", ImdbId: "tt4236770"), cancellationToken);

        //Assert
        var series = Assert.Single(search.Library, match => match.Work.Kind == MediaWorkKind.Series);
        Assert.Equal(MatchEvidence.Identifier, series.Evidence);
        Assert.Equal([new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt4236770", "Living room")], series.MatchedIdentifiers);
        Assert.Equal("Yellowstone", series.MatchedTitle!.Text);
        Assert.DoesNotContain(search.Library, match => match.Work.Kind == MediaWorkKind.Episode);
    }

    [Theory]
    // The series' title and its special's match alike: the series' own evidence, first on a tie.
    [InlineData(null, MatchEvidence.TitleOnly, 2018)]
    // Only the special's year is the one asked for: its stronger evidence.
    [InlineData("2017", MatchEvidence.TitleAndYear, 2017)]
    public async Task Create_by_a_title_of_the_series_and_of_its_episode_lists_the_series_on_the_stronger_its_own_on_a_tie(
        string? year, MatchEvidence expectedEvidence, int expectedMatchedYear)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var special = JellyfinSamples.EpisodeWith(
            ItemId('s'), "Yellowstone", ItemId('a'), 0, 1, "/media/tv-shows/Yellowstone/Specials/Yellowstone S00E01.mkv");
        special["ProductionYear"] = 2017;
        _jellyfin.Episodes.Add(special);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);

        //Act
        var search = await CreateSearchAsync(new SearchRequest("Yellowstone", year), cancellationToken);

        //Assert
        var series = Assert.Single(search.Library, match => match.Work.Kind == MediaWorkKind.Series);
        Assert.Equal(expectedEvidence, series.Evidence);
        Assert.Equal(("Yellowstone", (int?)expectedMatchedYear), (series.MatchedTitle!.Text, series.MatchedTitle.Year));
        Assert.DoesNotContain(search.Library, match => match.Work.Kind == MediaWorkKind.Episode);
    }

    [Fact]
    public async Task Create_lists_an_episode_of_no_series_as_a_work_of_its_own_with_its_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var search = await CreateSearchAsync(new SearchRequest("Flippin Fido"), cancellationToken);

        //Assert
        var match = Assert.Single(search.Library);
        Assert.Equal((MediaWorkKind.Episode, "Flippin Fido", MatchEvidence.TitleOnly), (match.Work.Kind, match.Work.Title, match.Evidence));
        Assert.Equal(new LibraryEpisodeDto(null, 1, 4), match.Work.Episode);
        Assert.Null(match.Work.Seasons);
        Assert.Single(match.Work.Versions);
    }

    [Theory]
    [InlineData("Clarkson's Farm", null)]
    [InlineData("Zzz", "tt10541088")]
    public async Task Create_lists_no_series_that_holds_no_episode(string text, string? imdbId)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var search = await CreateSearchAsync(new SearchRequest(text, ImdbId: imdbId), cancellationToken);

        //Assert
        Assert.Empty(search.Library);
        Assert.Equal(0, search.LibraryUnlistedCount);
        // It is in the snapshot all the same: the search leaves it out, not the import.
        await _factory.WithDbAsync(async db =>
            Assert.True(await db.MediaWorks.AnyAsync(work => work.Kind == MediaWorkKind.Series
                && db.MediaWorkTitles.Any(title => title.MediaWorkId == work.Id && title.Text == "Clarkson's Farm"), TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Create_counts_a_series_whose_episodes_hold_the_words_as_one_work_of_the_limit()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // 20 films and 25 episodes of one series hold the word: 21 works, one past the limit.
        for (var number = 1; number <= 20; number++)
        {
            _jellyfin.Movies.Add(JellyfinSamples.MovieWith(
                NumberedItemId(1, number), Numbered("Rain Movie", number), null, 2000 + number, $"/media/movies/{Numbered("Rain Movie", number)}.mkv"));
        }

        _jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('g'), "Autumn Stories", 2020, "/media/tv-shows/Autumn Stories"));
        for (var number = 1; number <= 25; number++)
        {
            _jellyfin.Episodes.Add(JellyfinSamples.EpisodeWith(
                NumberedItemId(2, number), Numbered("Rain Part", number), ItemId('g'), 1, number, $"/media/tv-shows/Autumn Stories/{number}.mkv"));
        }

        await new LibrarySnapshotSeeder(_factory.Scope).Sync(_livingRoom, _owner.User);

        //Act
        var search = await CreateSearchAsync(new SearchRequest("Rain"), cancellationToken);

        //Assert
        // By the title each card shows: the series first, once, then the first 19 films; the 20th counted.
        Assert.Equal(
            ["Series Autumn Stories", .. Enumerable.Range(1, 19).Select(number => $"Movie {Numbered("Rain Movie", number)}")],
            search.Library.Select(match => $"{match.Work.Kind} {match.Work.Title}"));
        Assert.All(search.Library, match => Assert.Equal(MatchEvidence.Words, match.Evidence));
        Assert.Equal(1, search.LibraryUnlistedCount);
        // Its episodes in their title order: the first holds the words first.
        Assert.Equal("Rain Part 01", search.Library[0].MatchedTitle!.Text);
        Assert.Equal([new LibrarySeasonDto(1, 25)], search.Library[0].Work.Seasons);
    }

    [Fact]
    public async Task Create_answers_a_series_card_in_the_library_block_with_its_seasons_and_no_place()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Yellowstone", "2018"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        var work = body["library"]![0]!["work"]!.AsObject();
        Assert.Equal("Series", (string)work["kind"]!);
        Assert.Null(work["episode"]);
        Assert.Equal(["number", "episodeCount"], work["seasons"]![0]!.AsObject().Select(property => property.Key));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static string ItemId(char letter) => new(letter, 32);

    private static string NumberedItemId(int group, int number) => string.Create(CultureInfo.InvariantCulture, $"{group}{number:d31}");

    private static string Numbered(string name, int number) => string.Create(CultureInfo.InvariantCulture, $"{name} {number:00}");

    /// <summary>A search by the member; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, request);
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
