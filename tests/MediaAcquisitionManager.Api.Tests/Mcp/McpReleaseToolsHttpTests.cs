using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mcp.Results;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Client;

namespace MediaAcquisitionManager.Api.Tests.Mcp;

// MAM-45 — search_releases and list_search_releases: the Search page's stored search for an assistant. A search asks
// every enabled source and stores the search, its sources and its releases exactly as the page does, and answers what the
// page shows that user — the library's works with their versions and files, every source with its status, a failed one's
// sentence and a file's path problem to the owner alone — with the first page of its releases, filtered and ordered as
// asked; its release ids are the stored ones, which a Read and download token of the same user sends through the HTTP
// API and another user's can't. list_search_releases pages, filters and orders that stored search without asking a
// source again, and answers another user's search, one more than a day old and one that never was with one not-found
// sentence. The page's sentences come back as isError results, a list value the list won't take before any source is
// asked; a summary says what was found and names each source that failed. Two feeds on one gateway: Toloka lists five
// releases built so that every order and filter tells them apart, and "Other" can't be reached.
public sealed class McpReleaseToolsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string SearchTool = "search_releases";
    private const string ListTool = "list_search_releases";
    private const string SearchesUrl = "/api/searches";
    private const string DownloadsUrl = "/api/downloads";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0045";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>The v1 info hash of <c>TorznabSamples.TorrentFile()</c>, which every release here downloads: qBittorrent's
    /// id for the torrent a send adds.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    // Spelled out rather than read from product code: what the assistant reads and tells its user.
    private const string UnreachableError =
        $"Couldn't reach Torznab at {OtherAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.";

    private const string SearchNotFoundError = "There is no search with this id among yours from the last day; run search_releases again.";

    private const string ReleaseMissingError = "This release isn't available any more: a search keeps what it found for a day. Search again.";

    /// <summary>Toloka's releases by a short name, in the order its feed lists them.</summary>
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["T0"] = "Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng",
        ["T1"] = "Interstellar (2014) BDRip 2160p HDR Ukr",
        ["T2"] = "Interstellar (2014) BDRip 720p Eng",
        ["T3"] = "Interstellar (2014) DVDRip",
        ["T4"] = "Interstellar (2014) WEBRip 1080p Ukr",
    };

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IPersonalAccessTokenSeeder _tokens = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private MediaWorkEntity _interstellar = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public McpReleaseToolsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A list's arguments as JSON, <c>{toloka}</c> and <c>{other}</c> standing for those sources' ids in the
    /// search, and the releases it lists in order.</summary>
    public static TheoryData<string, string[]> Lists => new()
    {
        // Seeders, the default: most first, a tie in the feed's order, none given last.
        { "{}", ["T1", "T4", "T0", "T2", "T3"] },
        { """{"sort":"Seeders"}""", ["T1", "T4", "T0", "T2", "T3"] },
        { """{"sort":"Largest"}""", ["T1", "T0", "T4", "T2", "T3"] },
        { """{"sort":"Smallest"}""", ["T2", "T4", "T0", "T1", "T3"] },
        { """{"sort":"Newest"}""", ["T0", "T1", "T4", "T2", "T3"] },
        { """{"sort":"Quality"}""", ["T1", "T4", "T0", "T2", "T3"] },
        // By source: the one that failed listed nothing.
        { """{"sourceId":"{toloka}"}""", ["T1", "T4", "T0", "T2", "T3"] },
        { """{"sourceId":"{other}"}""", [] },
        // By the kind its category tells: every one is in Movies.
        { """{"kind":"Movie"}""", ["T1", "T4", "T0", "T2", "T3"] },
        { """{"kind":"Tv"}""", [] },
        // By the audio language and the resolution a title claims, or claims none of.
        { """{"language":"ukr"}""", ["T1", "T4", "T0"] },
        { """{"language":"ENG"}""", ["T0", "T2"] },
        { """{"language":"unknown"}""", ["T3"] },
        { """{"quality":"1080"}""", ["T4", "T0"] },
        { """{"quality":"unknown"}""", ["T3"] },
        // Size bounds, both inclusive; a release of unknown size can't be shown to be inside either.
        { """{"minSizeBytes":12000000000}""", ["T1", "T4", "T0"] },
        { """{"maxSizeBytes":12000000000}""", ["T4", "T2"] },
        // Every filter at once, with an order.
        { """{"sourceId":"{toloka}","language":"ukr","quality":"1080","minSizeBytes":10000000000,"sort":"Largest"}""", ["T0", "T4"] },
    };

    /// <summary>A search's arguments the app doesn't take — the search's own, then the list's — and the sentence each
    /// answers.</summary>
    public static TheoryData<string, string> SearchRefusals => new()
    {
        { """{"text":""}""", "Enter the title to search for." },
        { """{"text":"   "}""", "Enter the title to search for." },
        { $$"""{"text":"{{new string('x', 201)}}"}""", "The search text can be at most 200 characters long." },
        { """{"text":"Interstellar","year":0}""", "“0” is not a year." },
        { """{"text":"Interstellar","year":-2014}""", "“-2014” is not a year." },
        { """{"text":"Interstellar","imdbId":"0816692"}""", "“0816692” is not an IMDb id, which looks like tt0816692." },
        { """{"text":"Interstellar","tmdbId":"tt0816692"}""", "“tt0816692” is not a TMDb id, which is a number such as 157336." },
        { """{"text":"Interstellar","season":-1}""", "“-1” is not a season. Use a season number, such as 1, or 0 for the specials." },
        { """{"text":"Interstellar","pageSize":0}""", "Page size must be between 1 and 100." },
        { """{"text":"Interstellar","pageSize":101}""", "Page size must be between 1 and 100." },
        { """{"text":"Interstellar","language":"uk"}""", "“uk” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { """{"text":"Interstellar","quality":"1080p"}""", "“1080p” is not a resolution. Use one such as 1080, or unknown." },
        { """{"text":"Interstellar","minSizeBytes":-1}""", "“-1” is not a size in bytes." },
        { """{"text":"Interstellar","minSizeBytes":10,"maxSizeBytes":5}""", "The smallest size can't be larger than the largest." },
    };

    /// <summary>A list's arguments the list doesn't take, and the sentence each answers.</summary>
    public static TheoryData<string, string> ListRefusals => new()
    {
        { """{"page":0}""", "Page must be 1 or more." },
        { """{"pageSize":0}""", "Page size must be between 1 and 100." },
        { """{"pageSize":101}""", "Page size must be between 1 and 100." },
        { """{"language":"ukrainian"}""", "“ukrainian” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { """{"quality":"hd"}""", "“hd” is not a resolution. Use one such as 1080, or unknown." },
        { """{"maxSizeBytes":-5}""", "“-5” is not a size in bytes." },
        { """{"minSizeBytes":10,"maxSizeBytes":5}""", "The smallest size can't be larger than the largest." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _tokens = new PersonalAccessTokenSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");

        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                Item(Titles["T0"], "t0", 30_000_000_000, 8, "Mon, 03 Aug 2026 00:00:00 +0000"),
                Item(Titles["T1"], "t1", 40_000_000_000, 33, "Thu, 20 Feb 2025 00:00:00 +0000"),
                Item(Titles["T2"], "t2", 5_000_000_000, 8, "Wed, 25 Mar 2015 00:00:00 +0000"),
                Item(Titles["T3"], "t3", null, null, null),
                Item(Titles["T4"], "t4", 12_000_000_000, 12, "Sun, 05 May 2019 00:00:00 +0000")),
        };
        var other = new FakeTorznabServer(OtherAddress, ApiKey)
        {
            Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
        };
        FakeTorznabServer.Arm(_factory.TorznabHttp, _toloka, other);
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);

        // The film in the library, its file under no path mapping: a path problem for the owner to read.
        var livingRoom = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey, pathMappings: [new PathMappingPair("/data", "/mnt/data")]);
        _interstellar = await new MediaWorkSeeder(_factory.Scope).CreateMovie(
            livingRoom, "Інтерстеллар", 2014, "157336", "tt0816692", "Interstellar", [40_000_000_000]);

        // What a send goes to.
        _qbittorrent = new FakeQBittorrentServer();
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await _integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── search_releases ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SearchReleases_answers_the_search_as_the_search_page_shows_it_to_the_same_user_with_its_first_page(bool asTheOwner)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = asTheOwner ? _owner : _member;
        var token = await _tokens.Create(user.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            SearchTool,
            new Dictionary<string, object?> { ["text"] = "Interstellar", ["year"] = 2014, ["pageSize"] = 2 },
            cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var found = result.ReadStructured<ReleaseSearchToolResult>();
        // What the Search page reads of the same search, with the same user's session.
        Assert.Equivalent(await ReadAsync<SearchResponse>($"{SearchesUrl}/{found.Search.Id}", user, cancellationToken), found.Search, strict: true);
        Assert.Equivalent(
            await ReadAsync<SearchReleaseListResponse>($"{SearchesUrl}/{found.Search.Id}/releases?pageSize=2", user, cancellationToken),
            found.Releases,
            strict: true);
        // Each source by itself: the one that failed with its sentence to the owner only (it names the gateway's address).
        Assert.Equal(
            [
                ("Other", (IntegrationCallStatus?)IntegrationCallStatus.Unreachable, asTheOwner ? UnreachableError : null, 0),
                ("Toloka", IntegrationCallStatus.Succeeded, null, 5),
            ],
            found.Search.Sources.Select(source => (source.Name, source.Status, source.Error, source.ReleaseCount)));
        // The library's copy, with its version and file — the file's path problem the owner's too.
        var match = Assert.Single(found.Search.Library);
        Assert.Equal((_interstellar.Id, MatchEvidence.TitleAndYear), (match.Work.Id, match.Evidence));
        var file = Assert.Single(Assert.Single(match.Work.Versions).Files);
        Assert.Equal(asTheOwner ? $"No path mapping of this integration covers “{file.ServicePath}”." : null, file.PathProblem);
        // The first page, most seeders first, each release matched to the copy.
        Assert.Equal(["T1", "T4"], NamesOf(found.Releases));
        Assert.Equal((5, 1, 2), (found.Releases.TotalCount, found.Releases.Page, found.Releases.PageSize));
        Assert.All(found.Releases.Items, release => Assert.Equal(_interstellar.Id, Assert.Single(release.LibraryMatches).WorkId));
        Assert.Equal(
            "Found 5 releases for “Interstellar” in 2 release sources; the first page lists 2; Other failed (Unreachable); the library holds 1 matching work.",
            result.ReadText());
    }

    [Fact]
    public async Task SearchReleases_stores_the_search_its_sources_and_its_releases_as_the_search_page_does()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            SearchTool,
            new Dictionary<string, object?> { ["text"] = " Interstellar ", ["year"] = 2014, ["imdbId"] = "tt0816692" },
            cancellationToken: cancellationToken);

        //Assert
        var found = result.ReadStructured<ReleaseSearchToolResult>();
        var everyRelease = await ReadAsync<SearchReleaseListResponse>($"{SearchesUrl}/{found.Search.Id}/releases?pageSize=100", _member, cancellationToken);
        await _factory.WithDbAsync(async db =>
        {
            var search = await db.Searches.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(
                (found.Search.Id, _member.User.Id, "Interstellar", (int?)2014, (string?)"tt0816692", (string?)null, (int?)null),
                (search.Id, search.UserId, search.Text, search.Year, search.ImdbId, search.TmdbId, search.Season));
            var sources = await db.SearchSources.AsNoTracking().OrderBy(source => source.Position).ToListAsync(cancellationToken);
            Assert.Equal(
                [
                    ("Other", 0, (IntegrationCallStatus?)IntegrationCallStatus.Unreachable, (string?)UnreachableError),
                    ("Toloka", 1, IntegrationCallStatus.Succeeded, null),
                ],
                sources.Select(source => (source.Name, source.Position, source.Status, TestTexts.En(source.Error))));
            Assert.All(sources, source => Assert.Equal(search.Id, source.SearchId));
            // Every release Toloka listed, under its source: the ids the tool and the page give are the stored rows'.
            var releases = await db.Releases.AsNoTracking().OrderBy(release => release.Position).ToListAsync(cancellationToken);
            Assert.Equal(["T0", "T1", "T2", "T3", "T4"], releases.Select(release => Titles.Single(title => title.Value == release.Title).Key));
            Assert.All(releases, release => Assert.Equal(sources[1].Id, release.SearchSourceId));
            Assert.Equal(releases.Select(release => release.Id).Order(), everyRelease.Items.Select(release => release.Id).Order());
            Assert.Subset(releases.Select(release => release.Id).ToHashSet(), found.Releases.Items.Select(release => release.Id).ToHashSet());
            // A search reads: it records nothing.
            Assert.Equal(0, await db.AuditEvents.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task SearchReleases_with_filters_and_an_order_answers_the_first_page_of_those_alone_and_stores_every_release()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            SearchTool,
            new Dictionary<string, object?> { ["text"] = "Interstellar", ["language"] = "ukr", ["quality"] = "1080", ["sort"] = "Largest" },
            cancellationToken: cancellationToken);

        //Assert
        var found = result.ReadStructured<ReleaseSearchToolResult>();
        Assert.Equal(["T0", "T4"], NamesOf(found.Releases));
        Assert.Equal((2, 20), (found.Releases.TotalCount, found.Releases.PageSize));
        Assert.Equal(
            "Found 5 releases for “Interstellar” in 2 release sources, 2 of them passing the filters; the first page lists 2; Other failed (Unreachable); the library holds 1 matching work.",
            result.ReadText());
        // The filters narrow the page, not the search: list_search_releases can still list the rest.
        await _factory.WithDbAsync(async db => Assert.Equal(5, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task SearchReleases_names_each_source_that_failed_or_was_not_asked_in_its_summary()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _toloka.Intercept = _ => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);
        await _integrations.CreateWithUnreadableApiKey("torznab", "Unreadable");
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(SearchTool, new Dictionary<string, object?> { ["text"] = "Ghostbusters" }, cancellationToken: cancellationToken);

        //Assert
        // Still a search, which says how each source answered.
        Assert.False(result.IsError);
        var found = result.ReadStructured<ReleaseSearchToolResult>();
        Assert.Equal(
            [
                ("Other", (IntegrationCallStatus?)IntegrationCallStatus.Unreachable, (ReleaseSourceNotSearchedReason?)null),
                ("Toloka", IntegrationCallStatus.AuthenticationFailed, null),
                ("Unreadable", null, ReleaseSourceNotSearchedReason.SecretUnreadable),
            ],
            found.Search.Sources.Select(source => (source.Name, source.Status, source.NotSearchedReason)));
        Assert.All(found.Search.Sources, source => Assert.Null(source.Error));
        Assert.Empty(found.Search.Library);
        Assert.Empty(found.Releases.Items);
        Assert.Equal(
            "Found 0 releases for “Ghostbusters” in 3 release sources; the first page lists 0; Other failed (Unreachable), Toloka failed (AuthenticationFailed), Unreadable was not searched (SecretUnreadable); the library holds no matching work.",
            result.ReadText());
    }

    [Theory]
    [MemberData(nameof(SearchRefusals))]
    public async Task SearchReleases_with_a_value_the_app_does_not_take_is_an_error_with_its_sentence_and_asks_and_stores_nothing(string arguments, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(SearchTool, ArgumentsOf(arguments), cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(expected, result.ReadText());
        Assert.Null(result.StructuredContent);
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.Searches.CountAsync(cancellationToken));
            Assert.Equal(0, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task SearchReleases_without_a_title_is_the_argument_sentence_and_asks_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(SearchTool, new Dictionary<string, object?> { ["year"] = 2014 }, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(
            "The arguments don't fit search_releases: The arguments dictionary is missing a value for the required parameter 'text'. (Parameter 'arguments') Check them against the tool's input schema and call it again.",
            result.ReadText());
        Assert.Empty(_factory.TorznabHttp.Requests);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Searches.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task SearchReleases_finds_releases_a_read_and_download_token_of_the_same_user_sends_through_the_http_api()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var found = (await client.CallToolAsync(SearchTool, new Dictionary<string, object?> { ["text"] = "Interstellar" }, cancellationToken: cancellationToken))
            .ReadStructured<ReleaseSearchToolResult>();
        var release = found.Releases.Items[0];

        //Act
        var response = await _factory.PostHttpRequest(DownloadsUrl, token.Secret, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            (release.Id, Titles["T1"], DownloadJobStatus.Submitted, (string?)SampleInfoHash),
            (job.ReleaseId, job.ReleaseTitle, job.Status, job.ClientJobId));
        Assert.Single(_qbittorrent.AddRequests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((job.Id, release.Id, (Guid?)_member.User.Id), (row.Id, row.ReleaseId, row.RequestedByUserId));
            // The release the tool listed is the stored one, of the member's search.
            var searchId = await db.Releases.AsNoTracking()
                .Where(stored => stored.Id == release.Id)
                .Join(db.SearchSources, stored => stored.SearchSourceId, source => (Guid?)source.Id, (stored, source) => source.SearchId)
                .SingleAsync(cancellationToken);
            Assert.Equal(found.Search.Id, searchId);
        });
    }

    [Fact]
    public async Task SearchReleases_finds_releases_another_users_token_can_not_send()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var membersToken = await _tokens.Create(_member.User);
        var ownersToken = await _tokens.Create(_owner.User, PersonalAccessTokenAccess.ReadAndDownload);
        await using var client = await _factory.ConnectMcpAsync(membersToken.Secret, cancellationToken: cancellationToken);
        var found = (await client.CallToolAsync(SearchTool, new Dictionary<string, object?> { ["text"] = "Interstellar" }, cancellationToken: cancellationToken))
            .ReadStructured<ReleaseSearchToolResult>();

        //Act
        var response = await _factory.PostHttpRequest(
            DownloadsUrl, ownersToken.Secret, new SendDownloadRequest(Guid.NewGuid(), found.Releases.Items[0].Id, _client.Id, _movies.Id));

        //Assert
        // A search is its creator's (MAM-12), so its releases are too, even to the owner.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ReleaseMissingError, await response.ReadErrorAsync(cancellationToken));
        Assert.Empty(_qbittorrent.AddRequests);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.DownloadJobs.CountAsync(cancellationToken)));
    }

    // ── list_search_releases ────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task ListSearchReleases_filters_and_orders_the_stored_search_as_the_search_page_does(string arguments, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(client, cancellationToken);

        //Act
        var result = await client.CallToolAsync(ListTool, ArgumentsOf(arguments, search), cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        var list = result.ReadStructured<SearchReleaseListResponse>();
        Assert.Equal(expected, NamesOf(list));
        Assert.Equal(expected.Length, list.TotalCount);
    }

    [Theory]
    [InlineData(1, new[] { "T1", "T4" })]
    [InlineData(2, new[] { "T0", "T2" })]
    [InlineData(3, new[] { "T3" })]
    [InlineData(4, new string[0])]
    public async Task ListSearchReleases_pages_the_stored_search_without_asking_a_source_again(int page, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(client, cancellationToken);
        var requestsBefore = _factory.TorznabHttp.Requests.Count;

        //Act
        var result = await client.CallToolAsync(
            ListTool,
            new Dictionary<string, object?> { ["searchId"] = search.Search.Id, ["page"] = page, ["pageSize"] = 2 },
            cancellationToken: cancellationToken);

        //Assert
        var list = result.ReadStructured<SearchReleaseListResponse>();
        Assert.Equal(expected, NamesOf(list));
        Assert.Equal((5, page, 2), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal($"This search holds 5 releases; page {page} lists {expected.Length}.", result.ReadText());
        Assert.Equal(requestsBefore, _factory.TorznabHttp.Requests.Count);
    }

    [Fact]
    public async Task ListSearchReleases_with_a_filter_says_how_many_pass_it_and_lists_twenty_a_page_by_default()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(client, cancellationToken);

        //Act
        var result = await client.CallToolAsync(
            ListTool, new Dictionary<string, object?> { ["searchId"] = search.Search.Id, ["language"] = "eng" }, cancellationToken: cancellationToken);

        //Assert
        var list = result.ReadStructured<SearchReleaseListResponse>();
        Assert.Equal((2, 1, 20), (list.TotalCount, list.Page, list.PageSize));
        Assert.Equal("2 releases of this search pass the filters; page 1 lists 2.", result.ReadText());
    }

    [Theory]
    [MemberData(nameof(ListRefusals))]
    public async Task ListSearchReleases_with_a_value_the_list_does_not_take_is_an_error_with_its_sentence(string arguments, string expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(client, cancellationToken);

        //Act
        var result = await client.CallToolAsync(ListTool, ArgumentsOf(arguments, search), cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(expected, result.ReadText());
        Assert.Null(result.StructuredContent);
    }

    [Theory]
    [InlineData(true, "{}")]
    [InlineData(false, "{}")]
    // Whose search first: another user's is not found whatever the other values say.
    [InlineData(true, """{"page":0,"language":"xx"}""")]
    public async Task ListSearchReleases_of_another_users_search_is_the_not_found_sentence_even_for_the_owner(bool ownerReadsTheMembers, string arguments)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var (searcher, reader) = ownerReadsTheMembers ? (_member, _owner) : (_owner, _member);
        var searchersToken = await _tokens.Create(searcher.User);
        var readersToken = await _tokens.Create(reader.User);
        await using var searchersClient = await _factory.ConnectMcpAsync(searchersToken.Secret, cancellationToken: cancellationToken);
        await using var readersClient = await _factory.ConnectMcpAsync(readersToken.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(searchersClient, cancellationToken);

        //Act
        var result = await readersClient.CallToolAsync(ListTool, ArgumentsOf(arguments, search), cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(SearchNotFoundError, result.ReadText());
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task ListSearchReleases_of_a_search_made_on_the_search_page_lists_it_for_the_same_user()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        var created = await _factory.PostHttpRequest(SearchesUrl, _member.AccessToken, new SearchRequest("Interstellar"));
        if (created.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync(cancellationToken)}");
        var search = (await created.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["searchId"] = search.Id }, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal(["T1", "T4", "T0", "T2", "T3"], NamesOf(result.ReadStructured<SearchReleaseListResponse>()));
    }

    [Fact]
    public async Task ListSearchReleases_of_a_search_a_day_old_still_lists_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(client, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(24));

        //Act
        var result = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["searchId"] = search.Search.Id }, cancellationToken: cancellationToken);

        //Assert
        Assert.False(result.IsError);
        Assert.Equal(5, result.ReadStructured<SearchReleaseListResponse>().TotalCount);
    }

    [Fact]
    public async Task ListSearchReleases_of_a_search_older_than_a_day_is_the_not_found_sentence_before_any_prune()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);
        var search = await SearchAsync(client, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        //Act
        var result = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["searchId"] = search.Search.Id }, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(SearchNotFoundError, result.ReadText());
        // Gone to the list as its releases may be at any moment now, whether or not a later search pruned it yet.
        await _factory.WithDbAsync(async db => Assert.Equal(search.Search.Id, (await db.Searches.AsNoTracking().SingleAsync(cancellationToken)).Id));
    }

    [Fact]
    public async Task ListSearchReleases_of_a_search_that_never_was_is_the_not_found_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = await _tokens.Create(_member.User);
        await using var client = await _factory.ConnectMcpAsync(token.Secret, cancellationToken: cancellationToken);

        //Act
        var result = await client.CallToolAsync(ListTool, new Dictionary<string, object?> { ["searchId"] = Guid.NewGuid() }, cancellationToken: cancellationToken);

        //Assert
        Assert.True(result.IsError);
        Assert.Equal(SearchNotFoundError, result.ReadText());
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One release Toloka lists: its download link on the feed's own indexer, which the fake answers with its .torrent,
    /// its page under <paramref name="topic"/>, a movie category, and only the facts given.
    /// </summary>
    private static string Item(string title, string topic, long? sizeBytes, int? seeders, string? published)
    {
        var facts = (sizeBytes is { } size ? $"<size>{size}</size>" : "")
            + (published is { } date ? $"<pubDate>{date}</pubDate>" : "")
            + (seeders is { } seeding ? $"""<torznab:attr name="seeders" value="{seeding}" />""" : "");
        return TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(TolokaAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://tracker.test/{topic}",
            extra: "<category>2000</category>" + facts);
    }

    /// <summary>A search for "Interstellar" through the tool, as <paramref name="client"/>'s token; throws unless it
    /// answered one, as a test that meant to search arranged something else.</summary>
    private static async Task<ReleaseSearchToolResult> SearchAsync(McpClient client, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(SearchTool, new Dictionary<string, object?> { ["text"] = "Interstellar" }, cancellationToken: cancellationToken);
        return result.IsError is true
            ? throw new InvalidOperationException($"search_releases answered an error: {result.ReadText()}")
            : result.ReadStructured<ReleaseSearchToolResult>();
    }

    /// <summary>
    /// <paramref name="json"/> as a tool's arguments, each value as the client would send it; with
    /// <paramref name="search"/>, its <c>searchId</c> added and <c>{toloka}</c> and <c>{other}</c> replaced by those
    /// sources' ids in it.
    /// </summary>
    private static Dictionary<string, object?> ArgumentsOf(string json, ReleaseSearchToolResult? search = null)
    {
        if (search is not null)
        {
            json = json
                .Replace("{toloka}", search.Search.Sources.Single(source => source.Name == "Toloka").Id.ToString(), StringComparison.Ordinal)
                .Replace("{other}", search.Search.Sources.Single(source => source.Name == "Other").Id.ToString(), StringComparison.Ordinal);
        }

        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .ToDictionary(argument => argument.Key, argument => ValueOf(argument.Value));
        if (search is not null)
            arguments["searchId"] = search.Search.Id;
        return arguments;
    }

    private static object? ValueOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetInt64(),
        _ => throw new ArgumentException($"No test passes a {value.ValueKind} argument.", nameof(value)),
    };

    /// <summary>The short names of a page's releases, in its order.</summary>
    private static string[] NamesOf(SearchReleaseListResponse list) =>
        [.. list.Items.Select(release => Titles.Single(title => title.Value == release.Title).Key)];

    /// <summary>The same answer as the web app reads it, through the HTTP API with <paramref name="user"/>'s session;
    /// throws unless it was read.</summary>
    private async Task<T> ReadAsync<T>(string url, AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest(url, user.AccessToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<T>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
