using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12 — "Available releases": the stored search's releases filtered, ordered and paged by the server
// (house rule 7) from what the search stored, never by asking a source again — by source, by the audio
// language and the resolution a title claims (or claims none of), by size bounds, all together; ordered
// by seeders (the default), size either way, publication or resolution, a value the listing doesn't give
// always last and ties always in the search's own order (the source's place, then the release's place in
// its source's answer). Two sources list eight releases built so that every order meets a tie and a
// missing value.
public sealed class SearchReleasesListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";

    /// <summary>
    /// The releases by a short name: "Other" sorts first, so its releases (O0–O2) come before Toloka's
    /// (T0–T4) on a tie, each source's in the order its feed listed them.
    /// </summary>
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["O0"] = "Interstellar (2014) WEB-DL 1080p Ukr",
        ["O1"] = "Інтерстеллар / Interstellar (2014) BDRip 480p Multi",
        ["O2"] = "Interstellar (2014) UHD 2160p",
        ["T0"] = "Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng",
        ["T1"] = "Interstellar (2014) BDRip 2160p HDR Ukr",
        ["T2"] = "Interstellar (2014) BDRip 720p Eng",
        ["T3"] = "Interstellar (2014) DVDRip",
        ["T4"] = "Interstellar (2014) WEBRip 1080p Ukr",
    };

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _other = null!;

    public SearchReleasesListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A query string, with <c>{toloka}</c> and <c>{other}</c> standing for those sources' ids in
    /// the search, and the releases it lists in order.</summary>
    public static TheoryData<string, string[]> Lists => new()
    {
        // Seeders, the default: most first, ties in the search's order, none given last.
        { "", ["O1", "T1", "T4", "O0", "T0", "T2", "O2", "T3"] },
        { "sort=seeders", ["O1", "T1", "T4", "O0", "T0", "T2", "O2", "T3"] },
        { "sort=SEEDERS", ["O1", "T1", "T4", "O0", "T0", "T2", "O2", "T3"] },
        { "sort=%20largest%20", ["O2", "T1", "T0", "T4", "O0", "T2", "O1", "T3"] },
        // Smallest first, and still the search's order on a tie and the unknown size last.
        { "sort=smallest", ["O1", "O0", "T2", "T4", "T0", "T1", "O2", "T3"] },
        { "sort=newest", ["T0", "T1", "O2", "O0", "T4", "O1", "T2", "T3"] },
        // The highest resolution first, then the most seeders; a resolution class without seeders last
        // within it.
        { "sort=quality", ["T1", "O2", "T4", "O0", "T0", "T2", "O1", "T3"] },
        // By source.
        { "source={other}", ["O1", "O0", "O2"] },
        { "source={toloka}", ["T1", "T4", "T0", "T2", "T3"] },
        // By the audio language a title claims, in any letter case; "Multi" is mul.
        { "language=ukr", ["T1", "T4", "O0", "T0"] },
        { "language=%20UKR%20", ["T1", "T4", "O0", "T0"] },
        { "language=eng", ["T0", "T2"] },
        { "language=mul", ["O1"] },
        { "language=Unknown", ["O2", "T3"] },
        // A language no title claims: nothing, not an error.
        { "language=fre", [] },
        // By the resolution a title claims, exactly: no bucketing.
        { "quality=1080", ["T4", "O0", "T0"] },
        { "quality=2160", ["T1", "O2"] },
        { "quality=UNKNOWN", ["T3"] },
        { "quality=360", [] },
        // Size bounds, both inclusive; a release of unknown size can't be shown to be inside either.
        { "minSizeBytes=5000000000", ["T1", "T4", "O0", "T0", "T2", "O2"] },
        { "maxSizeBytes=5000000000", ["O1", "O0", "T2"] },
        { "minSizeBytes=5000000000&maxSizeBytes=5000000000", ["O0", "T2"] },
        { "minSizeBytes=0", ["O1", "T1", "T4", "O0", "T0", "T2", "O2"] },
        // Every filter at once, with an order.
        { "source={toloka}&language=ukr&quality=1080&minSizeBytes=10000000000&sort=largest", ["T0", "T4"] },
        { "language=ukr&sort=quality", ["T1", "T4", "O0", "T0"] },
        // Blank values filter nothing.
        { "source=&language=&quality=&minSizeBytes=&maxSizeBytes=&sort=", ["O1", "T1", "T4", "O0", "T0", "T2", "O2", "T3"] },
    };

    /// <summary>A query string the list doesn't take and the sentence it answers.</summary>
    public static TheoryData<string, string> Refusals => new()
    {
        { "page=0", "Page must be 1 or more." },
        { "page=-1", "Page must be 1 or more." },
        { "pageSize=0", "Page size must be between 1 and 100." },
        { "pageSize=101", "Page size must be between 1 and 100." },
        { "source=nope", "“nope” is not a release source." },
        { "source=0123456789abcdef0123456789abcdef0123456789", "“0123456789abcdef0123456789abcdef…” is not a release source." },
        { "language=uk", "“uk” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { "language=ukrainian", "“ukrainian” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { "language=uk1", "“uk1” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { "language=%D1%83%D0%BA%D1%80", "“укр” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { "quality=1080p", "“1080p” is not a resolution. Use one such as 1080, or unknown." },
        { "quality=0", "“0” is not a resolution. Use one such as 1080, or unknown." },
        { "quality=10000", "“10000” is not a resolution. Use one such as 1080, or unknown." },
        { "quality=-1", "“-1” is not a resolution. Use one such as 1080, or unknown." },
        { "quality=hd", "“hd” is not a resolution. Use one such as 1080, or unknown." },
        { "minSizeBytes=-1", "“-1” is not a size in bytes." },
        { "minSizeBytes=1GB", "“1GB” is not a size in bytes." },
        { "maxSizeBytes=1.5", "“1.5” is not a size in bytes." },
        { "maxSizeBytes=99999999999999999999", "“99999999999999999999” is not a size in bytes." },
        { "minSizeBytes=10&maxSizeBytes=5", "The smallest size can't be larger than the largest." },
        { "sort=popular", "“popular” is not a sort order. Use seeders, largest, smallest, newest or quality." },
        { "sort=seeders%20desc", "“seeders desc” is not a sort order. Use seeders, largest, smallest, newest or quality." },
        // The first value that is wrong answers, in the order the list's controls go.
        { "source=nope&language=xx&sort=popular", "“nope” is not a release source." },
        { "language=xx&quality=yy", "“xx” is not a language code. Use a three-letter code such as ukr, or unknown." },
        { "minSizeBytes=x&maxSizeBytes=y", "“x” is not a size in bytes." },
        { "quality=yy&sort=popular", "“yy” is not a resolution. Use one such as 1080, or unknown." },
    };

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                Item(TolokaAddress, Titles["T0"], "t0", 30_000_000_000, 8, 9, "Mon, 03 Aug 2026 00:00:00 +0000"),
                Item(TolokaAddress, Titles["T1"], "t1", 40_000_000_000, 33, 3, "Thu, 20 Feb 2025 00:00:00 +0000"),
                Item(TolokaAddress, Titles["T2"], "t2", 5_000_000_000, 8, 4, "Wed, 25 Mar 2015 00:00:00 +0000"),
                Item(TolokaAddress, Titles["T3"], "t3", null, null, null, null),
                Item(TolokaAddress, Titles["T4"], "t4", 12_000_000_000, 12, 5, "Sun, 05 May 2019 00:00:00 +0000")),
        };
        _other = new FakeTorznabServer(OtherAddress, ApiKey)
        {
            SearchXml = TorznabSamples.Feed(
                Item(OtherAddress, Titles["O0"], "o0", 5_000_000_000, 8, 1, "Wed, 01 Jan 2020 00:00:00 +0000"),
                Item(OtherAddress, Titles["O1"], "o1", 2_000_000_000, 50, 2, "Wed, 25 Mar 2015 00:00:00 +0000"),
                Item(OtherAddress, Titles["O2"], "o2", 60_000_000_000, null, null, "Mon, 01 Jan 2024 00:00:00 +0000")),
        };
        FakeTorznabServer.Arm(_factory.TorznabHttp, _toloka, _other);
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Releases_filters_and_orders_the_searchs_releases_on_the_server(string query, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?{WithSourceIds(query, search)}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(expected.Select(name => Titles[name]), list.Items.Select(release => release.Title));
        Assert.Equal((expected.Length, 1, 25), (list.TotalCount, list.Page, list.PageSize));
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Releases_with_a_value_the_list_does_not_take_is_400_with_its_sentence(string query, string expectedError)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?{query}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, await response.ReadErrorAsync(cancellationToken));
    }

    [Fact]
    public async Task Releases_with_a_page_that_is_not_a_number_is_400()
    {
        //Arrange
        var search = await CreateSearchAsync(TestContext.Current.CancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?page=abc", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(1, new[] { "O1", "T1", "T4" })]
    [InlineData(2, new[] { "O0", "T0", "T2" })]
    [InlineData(3, new[] { "O2", "T3" })]
    // Past the last page: nothing, with the real count, not an error.
    [InlineData(4, new string[0])]
    [InlineData(int.MaxValue, new string[0])]
    public async Task Releases_pages_the_ordered_list(int page, string[] expected)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?page={page}&pageSize=3", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(expected.Select(name => Titles[name]), list.Items.Select(release => release.Title));
        Assert.Equal((8, page, 3), (list.TotalCount, list.Page, list.PageSize));
    }

    [Fact]
    public async Task Releases_pages_a_filtered_list_with_its_own_count()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?language=ukr&page=2&pageSize=3", _member.AccessToken);

        //Assert
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal([Titles["T0"]], list.Items.Select(release => release.Title));
        Assert.Equal(4, list.TotalCount);
    }

    [Fact]
    public async Task Releases_lists_each_release_with_what_its_listing_gives_and_what_its_title_claims()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);
        var toloka = search.Sources.Single(source => source.Name == "Toloka");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?sort=newest&pageSize=1", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var release = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(
            (toloka.Id, "Toloka", Titles["T0"], (long?)30_000_000_000, (int?)8, (int?)9, (DateTime?)new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc), "https://tracker.test/t0"),
            (release.SourceId!.Value, release.SourceName, release.Title, release.SizeBytes, release.Seeders, release.Peers, release.PublishedUtc, release.SourcePageUrl));
        // Through a gateway only the download says whether it is a .torrent or a magnet.
        Assert.Null(release.PayloadKind);
        Assert.Equal((1080, ReleaseMedium.BluRayRemux, (string?)null), (release.Claimed.Resolution!.Value, release.Claimed.Medium!.Value, release.Claimed.VideoCodec));
        Assert.Equal(["ukr", "eng"], release.Claimed.AudioLanguages);
        Assert.Equal(["eng"], release.Claimed.SubtitleLanguages);
        Assert.Empty(release.Claimed.DynamicRanges);
        Assert.Equal(["Interstellar"], release.ClaimedWork.Titles);
        Assert.Equal(2014, release.ClaimedWork.Year);
        Assert.Empty(release.Identifiers);
        Assert.Empty(release.LibraryMatches);
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().SingleAsync(row => row.Id == release.Id, cancellationToken);
            Assert.Equal((toloka.Id, Titles["T0"], (int?)0), (stored.SearchSourceId!.Value, stored.Title, stored.Position));
        });
    }

    [Fact]
    public async Task Releases_answers_the_lists_json_shape_with_enum_names_and_instants_in_utc()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?sort=newest", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        Assert.Equal(["items", "totalCount", "page", "pageSize"], body.Select(property => property.Key));
        var release = body["items"]![0]!.AsObject();
        Assert.Equal(
            [
                "id", "sourceId", "sourceName", "title", "description", "kind", "sizeBytes", "seeders", "peers", "publishedUtc", "sourcePageUrl",
                "payloadKind", "claimed", "claimedWork", "identifiers", "libraryMatches",
            ],
            release.Select(property => property.Key));
        Assert.Equal("2026-08-03T00:00:00Z", (string)release["publishedUtc"]!);
        Assert.Equal("BluRayRemux", (string)release["claimed"]!["medium"]!);
        Assert.Equal("Movie", (string)release["kind"]!);
        // MAM-32 — a movie's claim names no span and no seasons, present as null.
        Assert.Equal(["titles", "year", "lastYear", "episodes"], release["claimedWork"]!.AsObject().Select(property => property.Key));
        Assert.Null(release["claimedWork"]!["episodes"]);
        // A release with nothing listed: present, as null.
        var bare = body["items"]!.AsArray()[^1]!.AsObject();
        Assert.Equal(Titles["T3"], (string)bare["title"]!);
        Assert.Null(bare["description"]);
        Assert.Null(bare["publishedUtc"]);
        Assert.Null(bare["sizeBytes"]);
        Assert.Null(bare["seeders"]);
    }

    [Fact]
    public async Task Releases_lists_each_releases_description_as_the_search_stored_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // MAM-33 — Toloka's real answer, each listing with its own topic title as its description.
        _toloka.SearchXml = TorznabSamples.Search(TolokaAddress, ApiKey);
        _other.SearchXml = TorznabSamples.Feed();
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?sort=largest&pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount, list.TotalCount);
        // The largest, 72.9 GB.
        Assert.Equal("Інтерстеллар / Interstellar (2014) [2-Disc IMAX Edition] Blu-ray CEE Multi | Sub Multi", list.Items[0].Description);
        Assert.All(list.Items, release => Assert.StartsWith("Інтерстеллар / Interstellar (2014)", release.Description, StringComparison.Ordinal));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.Releases.AsNoTracking().ToDictionaryAsync(release => release.Id, release => release.Description, cancellationToken);
            Assert.All(list.Items, release => Assert.Equal(stored[release.Id], release.Description));
        });
    }

    [Fact]
    public async Task Releases_reads_what_the_search_stored_without_asking_a_source_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);
        var asked = _factory.TorznabHttp.Requests.Count;
        // A source that would answer otherwise now: a list read must not notice.
        _toloka.SearchXml = TorznabSamples.Feed();

        //Act
        var first = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?sort=largest", _member.AccessToken);
        var second = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?language=ukr", _member.AccessToken);

        //Assert
        Assert.Equal(8, (await ReadListAsync(first, cancellationToken)).TotalCount);
        Assert.Equal(4, (await ReadListAsync(second, cancellationToken)).TotalCount);
        Assert.Equal(asked, _factory.TorznabHttp.Requests.Count);
        await _factory.WithDbAsync(async db => Assert.Equal(8, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Releases_lists_only_the_searchs_own_releases()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlier = await CreateSearchAsync(cancellationToken);
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var all = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);
        // Another search's source, well formed: none of this search's releases came from it.
        var anotherSearchsSource = await _factory.GetHttpRequest(
            $"{BaseUrl}/{search.Id}/releases?source={earlier.Sources[0].Id}", _member.AccessToken);

        //Assert
        var list = await ReadListAsync(all, cancellationToken);
        Assert.Equal(8, list.TotalCount);
        Assert.All(list.Items, release => Assert.Contains(release.SourceId!.Value, search.Sources.Select(source => source.Id)));
        Assert.Equal(HttpStatusCode.OK, anotherSearchsSource.StatusCode);
        Assert.Equal(0, (await ReadListAsync(anotherSearchsSource, cancellationToken)).TotalCount);
        await _factory.WithDbAsync(async db => Assert.Equal(16, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Releases_of_a_source_no_search_has_is_an_empty_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var search = await CreateSearchAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?source={Guid.NewGuid()}", _member.AccessToken);

        //Assert
        // An id a page kept from before: nothing, not an error (the Library's rule for a library).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One release a feed lists: its download link on the feed's own indexer, its page under
    /// <paramref name="topic"/>, and only the facts given — a release listing no size, seeders or date
    /// says nothing of them.
    /// </summary>
    private static string Item(string feedAddress, string title, string topic, long? sizeBytes, int? seeders, int? peers, string? published)
    {
        var facts = (sizeBytes is { } size ? $"<size>{size}</size>" : "")
            + (published is { } date ? $"<pubDate>{date}</pubDate>" : "")
            + (seeders is { } seeding ? $"""<torznab:attr name="seeders" value="{seeding}" />""" : "")
            + (peers is { } peering ? $"""<torznab:attr name="peers" value="{peering}" />""" : "");
        return TorznabSamples.Item(
            title,
            $"{TorznabSamples.DownloadBaseFor(feedAddress)}download?apikey={ApiKey}&file={topic}",
            comments: $"https://tracker.test/{topic}",
            extra: "<category>2000</category>" + facts);
    }

    /// <summary>The query with <c>{toloka}</c> and <c>{other}</c> replaced by those sources' ids in
    /// <paramref name="search"/>.</summary>
    private static string WithSourceIds(string query, SearchResponse search) =>
        query
            .Replace("{toloka}", search.Sources.Single(source => source.Name == "Toloka").Id.ToString(), StringComparison.Ordinal)
            .Replace("{other}", search.Sources.Single(source => source.Name == "Other").Id.ToString(), StringComparison.Ordinal);

    /// <summary>A search for "Interstellar" by the member; throws unless it was created.</summary>
    private async Task<SearchResponse> CreateSearchAsync(CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    private static async Task<SearchReleaseListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
