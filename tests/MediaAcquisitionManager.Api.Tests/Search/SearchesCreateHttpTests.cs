using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12 — a stored search over real HTTP, against the real Torznab adapter with the network faked by
// the real Toloka answers: what it takes (the release search's rules and sentences — nothing stored and
// no source asked when one is broken), what it answers (the search as asked, every enabled source's
// answer by name, the library's matches, what the releases claim for the filters) and what it keeps (the
// search, one row per source with how it answered, every release linked to its source with its place,
// seeders, peers and ids). How the library and the sources are asked together is
// SearchesConcurrencyHttpTests', a failing source SearchesFailingSourcesHttpTests', who may read a
// search and for how long SearchesAccessHttpTests'.
public sealed class SearchesCreateHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _other = null!;

    public SearchesCreateHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey);
        _other = new FakeTorznabServer(OtherAddress, ApiKey);
        FakeTorznabServer.Arm(_factory.TorznabHttp, _toloka, _other);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Create_without_a_token_is_401_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Searches.CountAsync(cancellationToken)));
    }

    [Theory]
    [InlineData(null, null, null, null, "Enter the title to search for.")]
    [InlineData("   ", "2014", null, null, "Enter the title to search for.")]
    [InlineData("Inter\nstellar", null, null, null, "The search text can't contain line breaks or other control characters.")]
    [InlineData("Inter\tstellar", null, null, null, "The search text can't contain line breaks or other control characters.")]
    [InlineData("Interstellar", "20l4", null, null, "“20l4” is not a year.")]
    [InlineData("Interstellar", "0", null, null, "“0” is not a year.")]
    [InlineData("Interstellar", "10000", null, null, "“10000” is not a year.")]
    [InlineData("Interstellar", "-2014", null, null, "“-2014” is not a year.")]
    [InlineData("Interstellar", " 1234567890123456789012345678901234567890 ", null, null, "“12345678901234567890123456789012…” is not a year.")]
    [InlineData("Interstellar", null, "0816692", null, "“0816692” is not an IMDb id, which looks like tt0816692.")]
    [InlineData("Interstellar", null, null, "tt0816692", "“tt0816692” is not a TMDb id, which is a number such as 157336.")]
    // The first value that is wrong answers: the title, then the year, then the IMDb id, then the TMDb id.
    [InlineData("Interstellar", "20l4", "0816692", "tt0816692", "“20l4” is not a year.")]
    [InlineData("Interstellar", "2014", "0816692", "tt0816692", "“0816692” is not an IMDb id, which looks like tt0816692.")]
    public async Task Create_of_what_the_search_does_not_take_is_400_with_its_sentence_stores_nothing_and_asks_no_source(
        string? text, string? year, string? imdbId, string? tmdbId, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(text, year, imdbId, tmdbId));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_of_a_title_longer_than_200_characters_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(new string('a', 201)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The search text can be at most 200 characters long.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_measures_the_title_after_trimming_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var title = new string('a', 200);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest($"  {title}  "));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(title, (await ReadAsync(response, cancellationToken)).Text);
        await _factory.WithDbAsync(async db => Assert.Equal(title, (await db.Searches.AsNoTracking().SingleAsync(cancellationToken)).Text));
    }

    [Fact]
    public async Task Create_without_a_body_is_400_with_the_title_sentence_and_asks_no_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Authorization = new("Bearer", _member.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter the title to search for.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_with_an_empty_json_object_is_400_with_the_title_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl) { Content = JsonContent.Create(new { }) };
        request.Headers.Authorization = new("Bearer", _member.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter the title to search for.", await response.ReadErrorAsync(cancellationToken));
        await AssertNothingStoredAsync(cancellationToken);
    }

    [Fact]
    public async Task Create_as_a_member_is_201_with_the_search_as_asked_every_sources_answer_and_the_librarys_match()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interstellar = await SyncLibraryAsync(InterstellarMovie());
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var other = await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _other.SearchXml = TorznabSamples.Feed(
            OtherItem("Interstellar (2014) WEB-DL 2160p", "t1"),
            OtherItem("Interstellar (2014) WEBRip 1080p", "t2"));
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _member.AccessToken, new SearchRequest(" Interstellar ", " 2014 ", " TT0816692 ", " 157336 "));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(new Uri($"http://localhost{BaseUrl}/{body.Id}"), response.Headers.Location);
        // The search as asked, trimmed: an id keeps the letter case it was typed in.
        Assert.Equal(("Interstellar", 2014, "TT0816692", "157336", now), (body.Text, body.Year, body.ImdbId, body.TmdbId, body.CreatedUtc));
        // Every enabled source, by name, with how it answered and how many releases it listed.
        Assert.Equal(
            [
                (other.Id, "Other", (IntegrationCallStatus?)IntegrationCallStatus.Succeeded, (ReleaseSourceNotSearchedReason?)null, (string?)null, 2),
                (toloka.Id, "Toloka", IntegrationCallStatus.Succeeded, null, null, SampleMovieCount),
            ],
            body.Sources.Select(source => (source.IntegrationId!.Value, source.Name, source.Status, source.NotSearchedReason, source.Error, source.ReleaseCount)));
        Assert.Equal(2, body.Sources.Select(source => source.Id).Distinct().Count());
        // The library's copy of the film, by the id asked for.
        var match = Assert.Single(body.Library);
        Assert.Equal(interstellar, match.Work.Id);
        Assert.Equal(MatchEvidence.Identifier, match.Evidence);
    }

    [Fact]
    public async Task Create_answers_what_every_stored_release_claims_for_the_filters()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var facets = (await ReadAsync(response, cancellationToken)).Facets;
        // Toloka's answer: Ukrainian and English, one "Multi"; 4K, 1080p, 720p and a 640x360 frame size;
        // and titles that claim no language ("BDRip-AVC") or no resolution ("BDRip").
        Assert.Equal(["eng", "mul", "ukr"], facets.AudioLanguages);
        Assert.True(facets.HasUnknownAudioLanguage);
        Assert.Equal([2160, 1080, 720, 360], facets.Resolutions);
        Assert.True(facets.HasUnknownResolution);
    }

    [Fact]
    public async Task Create_and_get_answer_facets_of_their_own_releases_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var someoneElse = await new UserSeeder(_factory.Scope).CreateAuthorized("someone-else");
        // French audio and 480p, which no listing of Toloka's own answer claims: stored once by a plain
        // release search (MAM-10, linked to no search) and once by another user's search.
        var sample = _toloka.SearchXml;
        _toloka.SearchXml = TorznabSamples.Feed(TolokaItem("Interstellar (2014) DVDRip 480p FRA", "f1"));
        var plain = await _factory.PostHttpRequest("/api/releases/search", _member.AccessToken, new ReleaseSearchRequest("Interstellar"));
        var others = await _factory.PostHttpRequest(BaseUrl, someoneElse.AccessToken, new SearchRequest("Interstellar"));
        _toloka.SearchXml = sample;

        //Act
        var created = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));
        var read = await _factory.GetHttpRequest(created.Headers.Location!.AbsolutePath, _member.AccessToken);

        //Assert
        Assert.Equal(
            (HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.OK),
            (plain.StatusCode, others.StatusCode, created.StatusCode, read.StatusCode));
        // The French listing does claim both: they are the other search's facets.
        var othersFacets = (await ReadAsync(others, cancellationToken)).Facets;
        Assert.Equal(["fre"], othersFacets.AudioLanguages);
        Assert.Equal([480], othersFacets.Resolutions);
        // A search's facets are what its own releases claim, whatever else the table holds.
        foreach (var facets in new[] { (await ReadAsync(created, cancellationToken)).Facets, (await ReadAsync(read, cancellationToken)).Facets })
        {
            Assert.Equal(["eng", "mul", "ukr"], facets.AudioLanguages);
            Assert.Equal([2160, 1080, 720, 360], facets.Resolutions);
        }

        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.Releases.CountAsync(release => release.SearchSourceId == null, cancellationToken));
            Assert.Equal(1 + 1 + SampleMovieCount, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_answers_the_searchs_json_shape_with_enum_names_and_its_instant_in_utc()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLibraryAsync(InterstellarMovie());
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", "2014"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        Assert.Equal(
            ["id", "text", "year", "imdbId", "tmdbId", "season", "createdUtc", "library", "libraryUnlistedCount", "sources", "facets"],
            body.Select(property => property.Key));
        // MAM-12 — how many more works hold every word of the title than the list shows: none here.
        Assert.Equal(0, (int)body["libraryUnlistedCount"]!);
        // Present as null, not left away.
        Assert.True(body.ContainsKey("imdbId"));
        Assert.Null(body["imdbId"]);
        Assert.True(body.ContainsKey("season"));
        Assert.Null(body["season"]);
        Assert.EndsWith("Z", (string)body["createdUtc"]!, StringComparison.Ordinal);
        var match = body["library"]![0]!.AsObject();
        Assert.Equal(["work", "evidence", "matchedIdentifiers", "matchedTitle", "conflictingSchemes"], match.Select(property => property.Key));
        Assert.Equal("TitleAndYear", (string)match["evidence"]!);
        Assert.Equal(["text", "kind", "year", "sourceName"], match["matchedTitle"]!.AsObject().Select(property => property.Key));
        Assert.Equal("Original", (string)match["matchedTitle"]!["kind"]!);
        var source = body["sources"]![0]!.AsObject();
        Assert.Equal(
            ["id", "integrationId", "name", "status", "notSearchedReason", "error", "releaseCount"],
            source.Select(property => property.Key));
        Assert.Equal("Succeeded", (string)source["status"]!);
        Assert.Equal(
            ["audioLanguages", "hasUnknownAudioLanguage", "resolutions", "hasUnknownResolution", "kinds"],
            body["facets"]!.AsObject().Select(property => property.Key));
        Assert.Equal(["Movie"], body["facets"]!["kinds"]!.AsArray().Select(kind => (string)kind!));
    }

    [Fact]
    public async Task Create_stores_the_search_as_its_caller_asked_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(
            BaseUrl, _member.AccessToken, new SearchRequest(" Interstellar ", "2014", " tt0816692 ", " 157336 "));

        //Assert
        var body = await ReadAsync(response, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.Searches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(body.Id, search.Id);
            Assert.Equal(
                (_member.User.Id, "Interstellar", (int?)2014, "tt0816692", "157336", now),
                (search.UserId, search.Text, search.Year, search.ImdbId, search.TmdbId, search.CreatedUtc));
        });
    }

    [Fact]
    public async Task Create_stores_a_blank_year_and_ids_as_none()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", " ", "", "  "));

        //Assert
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(((int?)null, (string?)null, (string?)null), (body.Year, body.ImdbId, body.TmdbId));
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.Searches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(((int?)null, (string?)null, (string?)null), (search.Year, search.ImdbId, search.TmdbId));
        });
    }

    [Fact]
    public async Task Create_stores_how_each_source_answered_in_name_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var other = await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        var body = await ReadAsync(response, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var sources = await db.SearchSources.AsNoTracking().OrderBy(source => source.Position).ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    (body.Sources[0].Id, body.Id, (Guid?)other.Id, "Other", 0, (IntegrationCallStatus?)IntegrationCallStatus.Succeeded),
                    (body.Sources[1].Id, body.Id, toloka.Id, "Toloka", 1, IntegrationCallStatus.Succeeded),
                ],
                sources.Select(source => (source.Id, source.SearchId, source.IntegrationConnectionId, source.Name, source.Position, source.Status)));
            Assert.All(sources, source => Assert.Equal(((ReleaseSourceNotSearchedReason?)null, (string?)null), (source.NotSearchedReason, source.Error)));
        });
    }

    [Fact]
    public async Task Create_stores_every_release_linked_to_its_source_with_its_place_seeders_and_peers()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        await _factory.WithDbAsync(async db =>
        {
            var releases = await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken);
            Assert.Equal(SampleMovieCount, releases.Count);
            // Each in its place in Toloka's answer, from 0, the audio-only listing left out.
            Assert.Equal(Enumerable.Range(0, SampleMovieCount).Cast<int?>(), releases.Select(release => release.Position));
            Assert.All(releases, release =>
            {
                Assert.Equal((source.Id, toloka.Id, "Toloka", now), (release.SearchSourceId!.Value, release.SourceConnectionId!.Value, release.SourceName, release.CreatedUtc));
                // Toloka lists no registry ids.
                Assert.Equal(((string?)null, (string?)null), (release.ImdbId, release.TmdbId));
            });
            Assert.Equal(
                ("Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", (int?)8, (int?)8, (long?)36711232962),
                (releases[0].Title, releases[0].Seeders, releases[0].Peers, releases[0].SizeBytes));
            // The fifth listing of the answer is the fourth movie: the audio-only one before it is not stored.
            Assert.Equal(
                ("Interstellar (2014) BDRemux 4K 2160p HDR Dolby Vision H.265 [Hybrid] [IMAX Edition] Ukr/Eng | Sub Eng", (int?)77, (int?)77),
                (releases[3].Title, releases[3].Seeders, releases[3].Peers));
            Assert.Equal(("Interstellar (2014) [IMAX] BDRip 1080p H.265 Ukr/Eng | Sub Eng", (int?)89, (int?)90), (releases[4].Title, releases[4].Seeders, releases[4].Peers));
            Assert.Equal(("Interstellar (2014) BDRip", (int?)12), (releases[^1].Title, releases[^1].Seeders));
        });
    }

    [Fact]
    public async Task Create_stores_the_registry_ids_a_listing_gives_with_its_release()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _other.SearchXml = TorznabSamples.Feed(
            OtherItem("Interstellar (2014) 2160p", "t1", """<torznab:attr name="imdbid" value="tt0816692" /><torznab:attr name="tmdbid" value="157336" />"""),
            // Newznab's number without its leading zero, as Jackett writes it.
            OtherItem("Interstellar (2014) 1080p", "t2", """<torznab:attr name="imdb" value="816692" />"""),
            // Jackett's "none".
            OtherItem("Interstellar (2014) 720p", "t3", """<torznab:attr name="imdbid" value="0" /><torznab:attr name="tmdbid" value="0" />"""),
            OtherItem("Interstellar (2014) 480p", "t4"));

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [("tt0816692", "157336"), ("tt0816692", null), (null, null), ((string?)null, (string?)null)],
                (await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken))
                    .Select(release => (release.ImdbId, release.TmdbId))));
    }

    [Fact]
    public async Task Create_sends_each_source_what_the_release_search_sends()
    {
        //Arrange
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest(" Interstellar ", "2014", "tt0816692", "157336"));

        //Assert
        // One way to ask a source (Vasyl's decision 3): Toloka's movie search takes only text, so the ids
        // stay home and the year goes into the text, exactly as MAM-10's search sends it; MAM-32 — then its
        // TV search by the title alone. MAM-33 — the movie search in TV/Anime and TV/Documentary too.
        Assert.Equal(
            [
                $"{TolokaAddress}?t=caps&apikey={ApiKey}",
                $"{TolokaAddress}?t=movie&apikey={ApiKey}&q=Interstellar%202014&cat=2000,2020,2040,2070,5070,5080&limit=100",
                $"{TolokaAddress}?t=tvsearch&apikey={ApiKey}&q=Interstellar&cat=5000,5040,5050,5060,5070,5080&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Create_without_an_enabled_release_source_is_201_with_no_sources_and_keeps_the_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey, isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Empty(body.Sources);
        Assert.Empty(body.Library);
        Assert.Empty(body.Facets.AudioLanguages);
        Assert.Empty(body.Facets.Resolutions);
        Assert.False(body.Facets.HasUnknownAudioLanguage);
        Assert.False(body.Facets.HasUnknownResolution);
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(body.Id, (await db.Searches.AsNoTracking().SingleAsync(cancellationToken)).Id);
            Assert.Equal(0, await db.SearchSources.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_and_a_get_of_the_search_answer_the_same_body()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await SyncLibraryAsync(InterstellarMovie());
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var created = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", "2014"));
        var createdText = await created.Content.ReadAsStringAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(created.Headers.Location!.AbsolutePath, _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Nothing changed in between: the reload shows exactly what the search answered.
        Assert.Equal(createdText, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Contains("\"evidence\":\"TitleAndYear\"", createdText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_again_stores_a_new_search_with_releases_of_its_own()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var first = await ReadAsync(await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar")), cancellationToken);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var second = await ReadAsync(response, cancellationToken);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Sources[0].Id, second.Sources[0].Id);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(2, await db.Searches.CountAsync(cancellationToken));
            // A snapshot each (MAM-10's rule): the second search lists its own rows.
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(release => release.SearchSourceId == first.Sources[0].Id, cancellationToken));
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(release => release.SearchSourceId == second.Sources[0].Id, cancellationToken));
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>"Інтерстеллар" (2014), originally "Interstellar", with its IMDb and TMDb ids.</summary>
    private static JsonObject InterstellarMovie() =>
        JellyfinSamples.MovieWith(
            "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a",
            "Інтерстеллар",
            "Interstellar",
            2014,
            "/media/movies/Interstellar (2014)/Interstellar (2014) BDRip 1080p.mkv",
            "tt0816692",
            "157336");

    /// <summary>A release Toloka lists, its download link on its own feed and its page under
    /// <paramref name="topic"/>.</summary>
    private static string TolokaItem(string title, string topic) =>
        TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://toloka.to/{topic}",
            extra: "<category>2000</category>");

    /// <summary>A release "Other" lists, its download link on its own feed and its page under
    /// <paramref name="topic"/>.</summary>
    private static string OtherItem(string title, string topic, string extra = "") =>
        TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(OtherAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://other.test/{topic}",
            extra: "<category>2000</category>" + extra);

    /// <summary><paramref name="movies"/> as the library "Living room" holds them, stored the way a sync
    /// stores them; the id of the first one's work.</summary>
    private async Task<Guid> SyncLibraryAsync(params JsonObject[] movies)
    {
        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.AddRange(movies);
        jellyfin.Arm(_factory.JellyfinHttp);
        var integration = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(integration, _owner.User);

        var workId = Guid.Empty;
        var firstItemId = (string)movies[0]["Id"]!;
        await _factory.WithDbAsync(async db => workId = await db.MediaWorkTitles
            .Where(title => title.SourceItemId == firstItemId)
            .Select(title => title.MediaWorkId)
            .FirstAsync());
        return workId;
    }

    private async Task AssertNothingStoredAsync(CancellationToken cancellationToken) =>
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Searches.CountAsync(cancellationToken));
            Assert.Equal(0, await db.SearchSources.CountAsync(cancellationToken));
            Assert.Equal(0, await db.Releases.CountAsync(cancellationToken));
        });

    private static async Task<SearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
