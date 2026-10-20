using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-30 — the Search page with Jackett instead of Prowlarr (Vasyl's decisions 5 and 6), end to end over the real
// Torznab adapter and Jackett's recorded Toloka answers (FakeTorznabServer.Jackett): a film searched by its Ukrainian
// title, whose releases Jackett titles by its original one alone, matched through their descriptions to the library copy
// known by its Ukrainian title alone, a series' seasons with the episodes held, a Ukrainian-only
// film whose title Jackett's default "Strip Cyrillic Letters" left as "« »" matched through its description, a cartoon
// series the library knows by its Ukrainian title alone, and a key Jackett refuses said with where to copy the right one.
public sealed class SearchesJackettHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private const string InterstellarItemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";
    private const string ChervonaRutaItemId = "2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b2b";

    /// <summary>Jackett's wrong-key answer, worded with the fix (D16).</summary>
    private const string RefusedKeyError =
        "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _jackett = null!;

    public SearchesJackettHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        // Known by its Ukrainian title alone: no original title for Jackett's English titles to match.
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            InterstellarItemId, "Інтерстеллар", null, 2014, "/media/movies/Interstellar (2014).mkv", "tt0816692", "157336"));
        // A Ukrainian-only film, known by its Ukrainian title alone.
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(ChervonaRutaItemId, TorznabSamples.JackettUkrainianOnlyFilmTitle, null, 2026, "/media/movies/Potiah (2026).mkv"));
        // Yellowstone of 2018: two episodes of season 1, one of season 2, two of season 5.
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('a'), "Yellowstone", 2018, "/media/tv-shows/Yellowstone"));
        jellyfin.Episodes.Add(Episode('b', 'a', 1, 1));
        jellyfin.Episodes.Add(Episode('c', 'a', 1, 2));
        jellyfin.Episodes.Add(Episode('d', 'a', 2, 1));
        jellyfin.Episodes.Add(Episode('e', 'a', 5, 1));
        jellyfin.Episodes.Add(Episode('f', 'a', 5, 2));
        // The cartoon series of 2006, known by its Ukrainian title alone: one episode of season 1 and two of season 2.
        jellyfin.Series.Add(JellyfinSamples.SeriesWith(ItemId('g'), "Клуб Міккі Мауса", 2006, "/media/cartoons/Klub Mikki Mausa"));
        jellyfin.Episodes.Add(Episode('h', 'g', 1, 1));
        jellyfin.Episodes.Add(Episode('i', 'g', 2, 1));
        jellyfin.Episodes.Add(Episode('j', 'g', 2, 2));
        jellyfin.Arm(_factory.JellyfinHttp);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey), _owner.User);

        _jackett = FakeTorznabServer.Jackett(JackettAddress, ApiKey);
        _jackett.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_of_a_film_lists_jacketts_releases_matched_to_the_copy_the_library_knows_by_its_ukrainian_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateJackettAsync(ApiKey);

        //Act
        // Jackett answered t=movie&q=Інтерстеллар with the bytes it answered q=Interstellar (VendorSamples README).
        var search = await CreateSearchAsync(_member, "Інтерстеллар", "2014", cancellationToken);
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var source = Assert.Single(search.Sources);
        Assert.Equal(("Toloka", (IntegrationCallStatus?)IntegrationCallStatus.Succeeded), (source.Name, source.Status));
        // Jackett's 18 less the audio release and the trailer, which aren't in the movie categories asked.
        Assert.Equal(TorznabSamples.JackettItemCount - TorznabSamples.JackettNonMovieItemCount, releases.Count);
        var film = Assert.Single(search.Library);
        Assert.All(releases, release =>
        {
            var match = Assert.Single(release.LibraryMatches);
            Assert.Equal((film.Work.Id, MatchEvidence.TitleAndYear), (match.WorkId, match.Evidence));
            // The title gives the original title, which the library doesn't know; the description both, and its Ukrainian
            // title and year are what match.
            Assert.Equal(["Interstellar"], release.ClaimedWork.Titles);
            Assert.Equal(["Інтерстеллар", "Interstellar"], release.DescriptionWork!.Titles);
        });
        // Asked at the address Jackett gives, with /api, and never anywhere else.
        Assert.All(_factory.TorznabHttp.Requests, request => Assert.Equal("/api/v2.0/indexers/toloka/results/torznab/api", request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Create_of_a_ukrainian_only_film_matches_the_releases_whose_titles_kept_only_punctuation_through_their_descriptions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateJackettAsync(ApiKey);
        // The newest 90 in the movie categories, the two «Потяг «Червона Рута»» releases among them.
        _jackett.SearchXml = TorznabSamples.JackettLatestMovies(JackettAddress, ApiKey);

        //Act
        var search = await CreateSearchAsync(_member, TorznabSamples.JackettUkrainianOnlyFilmTitle, "2026", cancellationToken);
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var film = Assert.Single(search.Library);
        Assert.Equal(MatchEvidence.TitleAndYear, film.Evidence);
        Assert.Equal(TorznabSamples.JackettLatestItemCount, releases.Count);
        var matched = releases.Where(release => release.LibraryMatches.Count > 0).ToList();
        Assert.Equal(
            ["« » (2026) WEB-DL 1080p Ukr | Sub Ukr/Eng", "« » (2026) WEB-DL-AVC Ukr | Sub Ukr/Eng"],
            matched.Select(release => release.Title).Order(StringComparer.Ordinal));
        Assert.All(matched, release =>
        {
            Assert.Equal((film.Work.Id, MatchEvidence.TitleAndYear), (release.LibraryMatches[0].WorkId, release.LibraryMatches[0].Evidence));
            // The title read as punctuation, which matches nothing; the description as the film's title and year.
            Assert.Equal(["« »"], release.ClaimedWork.Titles);
            Assert.Equal([TorznabSamples.JackettUkrainianOnlyFilmTitle], release.DescriptionWork!.Titles);
            Assert.Equal(2026, release.DescriptionWork.Year);
        });
    }

    [Fact]
    public async Task Create_of_a_series_matches_jacketts_seasons_to_it_with_the_episodes_held()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateJackettAsync(ApiKey);
        _jackett.SearchXml = TorznabSamples.Feed();
        _jackett.TvSearchXml = TorznabSamples.JackettTvSearch(JackettAddress, ApiKey);

        //Act
        var search = await CreateSearchAsync(_member, "Yellowstone", null, cancellationToken);
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var show = Assert.Single(search.Library);
        // The twelve less the subtitles and the two archive sections; the documentary films of TV/Documentary are
        // movies, which match no series.
        Assert.Equal(TorznabSamples.JackettTvItemCount - TorznabSamples.JackettNonTvItemCount, releases.Count);
        Assert.Equal(TorznabSamples.TvFilmItemCount, releases.Count(release => release.Kind == ReleaseKind.Movie));
        Assert.Equal("TitleAndYear held 5", Describe(show, Assert.Single(releases, release => release.Title.StartsWith("Yellowstone (S1-5) (2018-2024)", StringComparison.Ordinal))));
        Assert.Equal("TitleAndYear held 3", Describe(show, Assert.Single(releases, release => release.Title.StartsWith("Yellowstone (S1-3) (2018-2020)", StringComparison.Ordinal))));
        Assert.All(
            releases.Where(release => release.Title.StartsWith("Yellowstone (S5) (2022-2024)", StringComparison.Ordinal)),
            release => Assert.Equal("TitleAndYear held 2", Describe(show, release)));
        // The BBC's Yellowstone of 2009: its season 1 began in 2009, not the show's 2018 — by its title alone.
        Assert.Equal("TitleOnly held 2", Describe(show, Assert.Single(releases, release => release.Title.StartsWith("Yellowstone (S1) (2009)", StringComparison.Ordinal))));
    }

    [Fact]
    public async Task Create_of_a_cartoon_series_by_its_ukrainian_title_matches_its_seasons_through_their_descriptions()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await CreateJackettAsync(ApiKey);
        _jackett.SearchXml = TorznabSamples.Feed();
        _jackett.TvSearchXml = TorznabSamples.JackettSearch(TorznabSamples.JackettTvCartoonUkrainianTitleSearchFile, JackettAddress, ApiKey);

        //Act
        var search = await CreateSearchAsync(_member, "Клуб Міккі Мауса", null, cancellationToken);
        var releases = await ReleasesAsync(search, cancellationToken);

        //Assert
        var show = Assert.Single(search.Library);
        // The book and the scan are in no TV category; every season is matched by the description's Ukrainian title and
        // years, which the English title alone couldn't.
        Assert.Equal(TorznabSamples.JackettTvCartoonUkrainianTitleItemCount - 2, releases.Count);
        Assert.Equal(
            [
                "Mickey Mouse Clubhouse (S1) (2006-2007) Satrip (S1) TitleAndYear held 1",
                "Mickey Mouse Clubhouse (S1-4) (2006-2016) WEB-DL 1080p/DVDRip-AVC Ukr/Eng | sub Ukr (S1-4) TitleAndYear held 3",
                "Mickey Mouse Clubhouse (S2) (2008-2010) SatRip (S2) TitleAndYear held 2",
                "Mickey Mouse Clubhouse (S2) (2008-2010) SatRip (S2) TitleAndYear held 2",
            ],
            releases.Select(release => $"{release.Title} {Describe(show, release)}").Order(StringComparer.Ordinal));
        Assert.All(releases, release => Assert.Equal(["Mickey Mouse Clubhouse"], release.ClaimedWork.Titles));
    }

    [Fact]
    public async Task Create_with_a_key_jackett_refuses_says_so_to_the_owner_with_where_to_copy_the_right_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await CreateJackettAsync("a-key-jackett-refuses");

        //Act
        var asOwner = await CreateSearchAsync(_owner, "Interstellar", "2014", cancellationToken);
        var asMember = await CreateSearchAsync(_member, "Interstellar", "2014", cancellationToken);

        //Assert
        // Jackett's recorded answer to a wrong key, HTTP 200 and error 100 to the caps request: nothing is searched.
        var ownersSource = Assert.Single(asOwner.Sources);
        Assert.Equal(((Guid?)jackett.Id, (IntegrationCallStatus?)IntegrationCallStatus.AuthenticationFailed), (ownersSource.IntegrationId, ownersSource.Status));
        Assert.Equal(RefusedKeyError, ownersSource.Error);
        Assert.Equal(0, ownersSource.ReleaseCount);
        // A member sees the status, not the owner's sentence (the MAM-8 rule).
        var membersSource = Assert.Single(asMember.Sources);
        Assert.Equal(((IntegrationCallStatus?)IntegrationCallStatus.AuthenticationFailed, (string?)null), (membersSource.Status, membersSource.Error));
        Assert.All(_factory.TorznabHttp.Requests, request => Assert.Contains("t=caps", request.Uri.Query, StringComparison.Ordinal));
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
        // The key that was sent, and the one Jackett holds, appear in no answer.
        var stored = await _factory.GetHttpRequest($"{BaseUrl}/{asOwner.Id}", _owner.AccessToken);
        var body = await stored.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("a-key-jackett-refuses", body, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
        // MAM-47 — read through the factory's options, which in the pseudo pass prove the sentence came through the renderer.
        Assert.Equal(
            RefusedKeyError,
            JsonSerializer.Deserialize<SearchResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!.Sources[0].Error);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Jackett's Toloka.to indexer as the owner adds it: its copied address and <paramref name="apiKey"/>.</summary>
    private Task<IntegrationConnectionEntity> CreateJackettAsync(string apiKey) =>
        _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: apiKey);

    private static string Describe(LibraryMatchDto show, SearchReleaseDto release)
    {
        var match = Assert.Single(release.LibraryMatches);
        Assert.Equal(show.Work.Id, match.WorkId);
        return string.Create(CultureInfo.InvariantCulture, $"{match.Evidence} held {match.HeldEpisodeCount}");
    }

    private static string ItemId(char letter) => new(letter, 32);

    /// <summary>An episode of a series of the test, its file named after its place.</summary>
    private static JsonObject Episode(char letter, char seriesLetter, int season, int episode) =>
        JellyfinSamples.EpisodeWith(
            ItemId(letter),
            string.Create(CultureInfo.InvariantCulture, $"Episode {episode}"),
            ItemId(seriesLetter),
            season,
            episode,
            string.Create(CultureInfo.InvariantCulture, $"/media/tv-shows/{seriesLetter}/S{season:00}E{episode:00}.mkv"));

    /// <summary>A search by <paramref name="user"/>; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(AuthorizedUser user, string text, string? year, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SearchRequest(text, year));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    /// <summary>Every release of <paramref name="search"/>, on one page; throws unless it was listed.</summary>
    private async Task<IReadOnlyList<SearchReleaseDto>> ReleasesAsync(SearchResponse search, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"The list answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items;
    }
}
