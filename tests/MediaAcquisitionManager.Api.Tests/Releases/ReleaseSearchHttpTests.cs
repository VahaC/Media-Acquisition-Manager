using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Releases;

// MAM-10 — a release search over real HTTP, against the real Torznab adapter with the network faked
// by the real Toloka answers (FakeTorznabServer): who may search, what the search takes, what it sends,
// what it answers and what it keeps. How several sources answer together is
// ReleaseSearchSourcesHttpTests', and the credentials ReleaseSearchSecretsHttpTests'.
public sealed class ReleaseSearchHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/releases/search";
    private const string FeedAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public ReleaseSearchHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Search_without_a_token_is_401()
    {
        //Arrange
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, body: new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Theory]
    [InlineData(null, null, null, null, "Enter the title to search for.")]
    [InlineData("   ", "2014", null, null, "Enter the title to search for.")]
    [InlineData("Inter\nstellar", null, null, null, "The search text can't contain line breaks or other control characters.")]
    [InlineData("Interstellar", "20l4", null, null, "“20l4” is not a year.")]
    [InlineData("Interstellar", "0", null, null, "“0” is not a year.")]
    [InlineData("Interstellar", null, "0816692", null, "“0816692” is not an IMDb id, which looks like tt0816692.")]
    [InlineData("Interstellar", null, null, "tt0816692", "“tt0816692” is not a TMDb id, which is a number such as 157336.")]
    public async Task Search_of_what_the_search_does_not_take_is_400_with_its_sentence_and_asks_no_source(
        string? text, string? year, string? imdbId, string? tmdbId, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest(text, year, imdbId, tmdbId));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_of_a_title_too_long_is_400_with_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest(new string('a', 201)));

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The search text can be at most 200 characters long.", await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Search_without_a_body_is_400_with_the_title_sentence_and_asks_no_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);
        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Authorization = new("Bearer", _member.AccessToken);

        //Act
        var response = await _factory.SendHttpRequest(request);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter the title to search for.", await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Search_with_an_empty_json_object_is_400_with_the_title_sentence()
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
    }

    [Fact]
    public async Task Search_as_a_member_lists_the_movie_releases_toloka_answered()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar", "2014"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var body = JsonSerializer.Deserialize<ReleaseSearchResponse>(text, MediaAcquisitionManagerApiFactory.JsonOptions);
        // The instant leaves as UTC with its Z, or a browser reads it as local time (house rule 9).
        using (var document = JsonDocument.Parse(text))
        {
            var firstRelease = document.RootElement.GetProperty("sources")[0].GetProperty("releases")[0];
            Assert.Equal("2026-08-02T21:00:00Z", firstRelease.GetProperty("publishedUtc").GetString());
        }

        var source = Assert.Single(body!.Sources);
        Assert.Equal(toloka.Id, source.IntegrationId);
        Assert.Equal("Toloka", source.Name);
        Assert.Equal(IntegrationCallStatus.Succeeded, source.Status);
        Assert.Null(source.NotSearchedReason);
        Assert.Null(source.Error);
        // Every release but the audio-only one (3020): the search asks for movies (ADR 0004).
        Assert.Equal(TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount, source.Releases.Count);
        Assert.DoesNotContain(source.Releases, release => release.Categories.Any(category => category.Id == TorznabSamples.NonMovieCategoryId));
        Assert.Equal(source.Releases.Count, source.Releases.Select(release => release.Id).Distinct().Count());

        var first = source.Releases[0];
        Assert.Equal("Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", first.Title);
        Assert.Equal(36711232962, first.SizeBytes);
        Assert.Equal(8, first.Seeders);
        Assert.Equal(8, first.Peers);
        Assert.Equal(new DateTime(2026, 8, 2, 21, 0, 0, DateTimeKind.Utc), first.PublishedUtc);
        // The topic link without the owner's forum session (ADR 0004).
        Assert.Equal("https://toloka.to/t699400", first.SourcePageUrl);
        Assert.Equal([new ReleaseCategoryDto("2040", "Movies/HD"), new ReleaseCategoryDto("100096", "|-Фільми в HD")], first.Categories);
        // Through a gateway, only the download says whether it is a .torrent or a magnet.
        Assert.Null(first.PayloadKind);
        Assert.Equal(1080, first.Claimed.Resolution);
        Assert.Equal(ReleaseMedium.BluRayRemux, first.Claimed.Medium);
        Assert.Null(first.Claimed.VideoCodec);
        Assert.Empty(first.Claimed.DynamicRanges);
        Assert.Equal(["ukr", "eng"], first.Claimed.AudioLanguages);
        Assert.Equal(["eng"], first.Claimed.SubtitleLanguages);
    }

    [Fact]
    public async Task Search_sends_the_title_and_year_to_the_movie_search_and_the_title_to_the_tv_search_each_in_its_categories()
    {
        //Arrange
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);

        //Act
        await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest(" Interstellar ", "2014", "tt0816692", "157336"));

        //Assert
        // The capabilities, then one movie search: Toloka's movie search takes only text, so the ids stay
        // home, the year goes into the text, and the first query found releases. MAM-32 — then its TV
        // search, by the title without the year (ADR 0020), in the TV categories. MAM-33 — the movie search
        // asks TV/Anime and TV/Documentary too, where Toloka files anime and documentary films.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&q=Interstellar%202014&cat=2000,2020,2040,2070,5070,5080&limit=100",
                $"{FeedAddress}?t=tvsearch&apikey={ApiKey}&q=Interstellar&cat=5000,5040,5050,5060,5070,5080&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Search_of_a_source_that_takes_ids_sends_the_imdb_id_alone_first()
    {
        //Arrange
        await _integrations.Create("torznab", "Jackett", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey) { CapsXml = CapsTakingIds }.Arm(_factory.TorznabHttp);

        //Act
        await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar", "2014", " TT0816692 ", " 157336 "));

        //Assert
        // The id trimmed, without its prefix, IMDb before TMDb, and alone: it found releases, so no
        // title tier follows.
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&imdbid=0816692&cat=2000,2020,2040,2070&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Search_whose_id_finds_nothing_searches_the_title_and_the_year_it_takes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Jackett", FeedAddress, apiKey: ApiKey);
        var feed = new FakeTorznabServer(FeedAddress, ApiKey) { CapsXml = CapsTakingIds };
        feed.Intercept = request => request.RequestUri!.Query.Contains("tmdbid=", StringComparison.Ordinal)
            ? VendorResponses.Text(TorznabSamples.Feed(), HttpStatusCode.OK, "application/rss+xml")
            : null;
        feed.Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar", "2014", TmdbId: "157336"));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount, Assert.Single(body!.Sources).Releases.Count);
        Assert.Equal(
            [
                $"{FeedAddress}?t=caps&apikey={ApiKey}",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&tmdbid=157336&cat=2000,2020,2040,2070&limit=100",
                $"{FeedAddress}?t=movie&apikey={ApiKey}&q=Interstellar&year=2014&cat=2000,2020,2040,2070&limit=100",
            ],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task Search_that_finds_nothing_by_title_and_year_searches_the_title_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var toloka = new FakeTorznabServer(FeedAddress, ApiKey);
        toloka.Intercept = request => request.RequestUri!.Query.Contains("q=Interstellar%202014", StringComparison.Ordinal)
            ? VendorResponses.Text(TorznabSamples.Feed(), HttpStatusCode.OK, "application/rss+xml")
            : null;
        toloka.Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar", "2014"));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount, Assert.Single(body!.Sources).Releases.Count);
        // MAM-32 — then the TV search, which the movie tiers' answers don't stop.
        Assert.Equal(
            ["t=caps", "q=Interstellar%202014", "q=Interstellar&", "t=tvsearch"],
            _factory.TorznabHttp.Requests.Select(request => request.Uri.Query switch
            {
                var query when query.Contains("t=caps", StringComparison.Ordinal) => "t=caps",
                var query when query.Contains("t=tvsearch", StringComparison.Ordinal) => "t=tvsearch",
                var query when query.Contains("q=Interstellar%202014", StringComparison.Ordinal) => "q=Interstellar%202014",
                var query when query.Contains("q=Interstellar&", StringComparison.Ordinal) => "q=Interstellar&",
                var query => query,
            }));
    }

    [Fact]
    public async Task Search_stores_each_release_with_its_source_and_its_download_link_sealed_without_the_key()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);
        var now = _factory.Time.GetUtcNow().UtcDateTime;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var answered = Assert.Single((await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Sources).Releases;
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(answered.Select(release => release.Id).Order(), stored.Select(release => release.Id).Order());

            var first = Assert.Single(stored, release => release.Id == answered[0].Id);
            Assert.Equal(ProvenanceSourceKind.ReleaseSearch, first.SourceKind);
            Assert.Equal(toloka.Id, first.SourceConnectionId);
            Assert.Equal("Toloka", first.SourceName);
            Assert.Equal("https://toloka.to/t699400", first.SourceItemId);
            Assert.Equal(answered[0].Title, first.Title);
            Assert.Equal(36711232962, first.SizeBytes);
            Assert.Equal(new DateTime(2026, 8, 2, 21, 0, 0), first.PublishedUtc);
            Assert.Equal("https://toloka.to/t699400", first.SourcePageUrl);
            Assert.Null(first.PayloadKind);
            Assert.Null(first.InfoHash);
            Assert.Equal(now, first.CreatedUtc);
            Assert.Equal(now, first.ObservedUtc);

            // Sealed to its own row, and holding the placeholder where the feed put its key (ADR 0007).
            var encryption = _factory.Resolve<IEncryptionService>();
            Assert.True(encryption.TryDecrypt(first.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(first.Id), out var locator));
            Assert.StartsWith("http://prowlarr.test:9696/1/download?apikey=%7Bapi-key%7D&link=REDACTED&file=Interstellar", locator, StringComparison.Ordinal);
            var another = stored.First(release => release.Id != first.Id);
            Assert.False(encryption.TryDecrypt(first.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(another.Id), out _));
            Assert.All(stored, release =>
            {
                Assert.True(encryption.TryDecrypt(release.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(release.Id), out var link));
                Assert.DoesNotContain(ApiKey, link, StringComparison.Ordinal);
                Assert.Contains("apikey=%7Bapi-key%7D", link, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    public async Task Search_stores_each_releases_place_seeders_peers_and_listed_ids_outside_any_stored_search()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        const string movie = "<category>2000</category>";
        new FakeTorznabServer(FeedAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                TorznabSamples.Item(
                    "Interstellar (2014) 2160p",
                    $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=1",
                    extra: movie + """<torznab:attr name="seeders" value="33" /><torznab:attr name="peers" value="40" /><torznab:attr name="imdbid" value="tt0816692" /><torznab:attr name="tmdbid" value="157336" />"""),
                TorznabSamples.Item("Interstellar (2014) 1080p", $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=2", extra: movie)),
        }.Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        // MAM-12 — one store for both searches: a plain search's rows carry what a stored search's list
        // filters and sorts by, linked to no stored search.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [
                    ((int?)0, (int?)33, (int?)40, "tt0816692", "157336", (Guid?)null),
                    (1, null, null, null, (string?)null, null),
                ],
                (await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken))
                    .Select(release => (release.Position, release.Seeders, release.Peers, release.ImdbId, release.TmdbId, release.SearchSourceId))));
    }

    [Fact]
    public async Task Search_answers_and_stores_each_releases_description_when_it_says_more_than_the_title()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        const string movie = "<category>2000</category>";
        var link = $"{TorznabSamples.DownloadBaseFor(FeedAddress)}download?file=";
        new FakeTorznabServer(FeedAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                TorznabSamples.Item("Interstellar (2014) BDRip", link + "1", extra: movie + "<description>Інтерстеллар / Interstellar (2014) BDRip</description>"),
                TorznabSamples.Item("Interstellar (2014) 720p", link + "2", extra: movie),
                TorznabSamples.Item("Interstellar (2014) 1080p", link + "3", extra: movie + "<description>Interstellar (2014) 1080p</description>")),
        }.Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        // MAM-33 — Toloka's own topic title, Ukrainian title first; none when the listing gives none or the
        // title again.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var releases = Assert.Single((await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(
            MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Sources).Releases;
        string?[] expected = ["Інтерстеллар / Interstellar (2014) BDRip", null, null];
        Assert.Equal(expected, releases.Select(release => release.Description));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                expected,
                await db.Releases.AsNoTracking().OrderBy(release => release.Position).Select(release => release.Description).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_of_a_magnet_release_answers_and_stores_its_kind_and_info_hash()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        const string magnet = "magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&dn=Interstellar";
        new FakeTorznabServer(FeedAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(TorznabSamples.Item(
                "Interstellar (2014) WEB-DL 2160p",
                magnet,
                extra: """<category>2000</category><torznab:attr name="infohash" value="0123456789ABCDEF0123456789ABCDEF01234567" />""")),
        }.Arm(_factory.TorznabHttp);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var body = await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var release = Assert.Single(Assert.Single(body!.Sources).Releases);
        Assert.Equal(ReleasePayloadKind.Magnet, release.PayloadKind);
        Assert.Equal(2160, release.Claimed.Resolution);
        Assert.Equal(ReleaseMedium.WebDownload, release.Claimed.Medium);
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(ReleasePayloadKind.Magnet, stored.PayloadKind);
            Assert.Equal("0123456789abcdef0123456789abcdef01234567", stored.InfoHash);
            Assert.True(_factory.Resolve<IEncryptionService>().TryDecrypt(
                stored.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(stored.Id), out var locator));
            Assert.Equal(magnet, locator);
        });
    }

    [Fact]
    public async Task Search_again_stores_new_releases_and_keeps_the_earlier_ones()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);
        var (firstStatus, first) = await SearchAsync(_member, cancellationToken);

        //Act
        var (secondStatus, second) = await SearchAsync(_member, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, firstStatus);
        Assert.Equal(HttpStatusCode.OK, secondStatus);
        Assert.NotEmpty(first);
        // A job points at the release it sent: the evidence must not change when the listing is found
        // again, so each search is a snapshot of its own.
        Assert.Empty(first.Intersect(second));
        await _factory.WithDbAsync(async db => Assert.Equal(first.Count + second.Count, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_removes_releases_older_than_a_day_that_no_job_sent()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", FeedAddress, apiKey: ApiKey);
        var client = await _integrations.Create("qbittorrent", "Downloads", "http://qbittorrent.test:8080");
        new FakeTorznabServer(FeedAddress, ApiKey).Arm(_factory.TorznabHttp);
        var unused = await new ReleaseSeeder(_factory.Scope).Create(toloka);
        var sent = await new DownloadJobSeeder(_factory.Scope).Create(client);
        _factory.Time.Advance(TimeSpan.FromHours(1));
        var recent = await new ReleaseSeeder(_factory.Scope).Create(toloka);
        // Half an hour either side of the day: the unused release is 24.5 hours old, the recent one 23.5.
        _factory.Time.Advance(TimeSpan.FromMinutes(23 * 60 + 30));
        // A day later the morning's access token has long expired: the member signs in again.
        var member = await new UserSeeder(_factory.Scope).CreateSession(_member.User);

        //Act
        var (status, found) = await SearchAsync(member, cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, status);
        await _factory.WithDbAsync(async db =>
        {
            var left = await db.Releases.AsNoTracking().Select(release => release.Id).ToListAsync(cancellationToken);
            Assert.DoesNotContain(unused.Id, left);
            // A job's release is its evidence of what was sent (Restrict, MAM-7); a day-old one stays.
            Assert.Contains(sent.ReleaseId, left);
            Assert.Contains(recent.Id, left);
            Assert.Superset(found.ToHashSet(), left.ToHashSet());
            Assert.Equal(found.Count + 2, left.Count);
        });
    }

    [Fact]
    public async Task Search_without_an_enabled_release_source_is_200_with_no_sources()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("jellyfin", "Living room", "http://jellyfin.test:8096", apiKey: "planted-jellyfin-key-0001");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Empty(body!.Sources);
        Assert.Empty(_factory.JellyfinHttp.Requests);
    }

    /// <summary>The search's status and the ids of the releases it answered (none unless it succeeded).</summary>
    private async Task<(HttpStatusCode Status, List<Guid> Ids)> SearchAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new ReleaseSearchRequest("Interstellar"));
        if (!response.IsSuccessStatusCode)
            return (response.StatusCode, []);

        var body = await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        return (response.StatusCode, [.. body!.Sources.SelectMany(source => source.Releases).Select(release => release.Id)]);
    }

    /// <summary>A gateway indexer whose movie search takes every parameter (Jackett's shape for a tracker
    /// that knows IMDb and TMDb ids), with Toloka's movie categories.</summary>
    private static string CapsTakingIds =>
        """
        <caps>
          <searching>
            <search available="yes" supportedParams="q" />
            <movie-search available="yes" supportedParams="q,imdbid,tmdbid,year" />
          </searching>
          <categories>
            <category id="2000" name="Movies"><subcat id="2020" name="Movies/Other" /><subcat id="2040" name="Movies/HD" /><subcat id="2070" name="Movies/DVD" /></category>
          </categories>
        </caps>
        """;
}
