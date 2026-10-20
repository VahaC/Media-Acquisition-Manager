using System.Globalization;
using System.Net;
using System.Security;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-10 — a release search as the contract runs it: the request it sends, the real answers of Toloka
// through Prowlarr (VendorSamples/Torznab, moved to this test's feed by TorznabSamples) read field by
// field, hand-written items for what they don't show, and every failure a status and a sentence. What
// becomes of a listing's links and id is TorznabAdapterSearchLinksTests'.
public sealed class TorznabAdapterSearchTests : IDisposable
{
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";
    private const string DownloadLink = "http://prowlarr.test:9696/1/download?apikey=0f9e8d7c6b5a49382716a5b4c3d2e1f0&link=Q2ZESjhQcm90&file=x";

    private const string NotSearchResultsError =
        "The service at this address answered, but not with Torznab search results. Check that the address is the feed's Torznab URL.";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    /// <summary>The caps sample's movie categories, as <c>ReleaseSearchPlanning</c> asks for them.</summary>
    private static readonly string[] MovieCategories = ["2000", "2020", "2040", "2070"];

    /// <summary>Queries no core code builds, by what is wrong with them.</summary>
    private static readonly Dictionary<string, ReleaseSearchQuery> MistakenQueries = new(StringComparer.Ordinal)
    {
        ["a limit of zero"] = Query(limit: 0),
        ["neither text nor an identifier"] = Query(text: null),
        // MAM-33 — nor in the movie and TV searches: only the newest releases are asked with no text, and
        // anything else without it would list them too, read as an answer to a title.
        ["a movie search with neither text nor an identifier"] = Query(ReleaseSearchMode.Movie, text: null),
        ["a TV search with neither text nor an identifier"] = Query(ReleaseSearchMode.Tv, text: null),
        ["blank text"] = Query(text: "  "),
        ["an identifier in a general search"] = Query(identifiers: [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692")]),
        ["a category that isn't a number"] = Query(categories: ["2000", "20a0"]),
        ["a category with a line break"] = Query(categories: ["2000\n"]),
        ["a malformed IMDb id"] = Query(ReleaseSearchMode.Movie, identifiers: [new MediaIdentifier(MediaIdentifierScheme.Imdb, "0816692")]),
        ["a malformed TMDb id"] = Query(ReleaseSearchMode.Movie, identifiers: [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "tt157336")]),
        ["two IMDb ids"] = Query(
            ReleaseSearchMode.Movie,
            identifiers: [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816693")]),
        ["an unknown mode"] = Query((ReleaseSearchMode)7),
        // MAM-33 — the newest releases are asked with their categories alone (ReleaseSearchPlanning.ForLatest).
        ["the newest releases with text"] = Query(ReleaseSearchMode.Latest),
        ["the newest releases with an identifier"] = Query(
            ReleaseSearchMode.Latest, text: null, identifiers: [new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692")]),
        ["the newest releases with a year"] = Query(ReleaseSearchMode.Latest, text: null) with { Year = 2014 },
        ["the newest releases of a season"] = Query(ReleaseSearchMode.Latest, text: null) with { Season = 1 },
    };

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public static TheoryData<string> Mistakes => new(MistakenQueries.Keys);

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Search_sends_a_movie_search_with_every_parameter_it_is_given()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(TorznabSamples.Feed());
        var query = new ReleaseSearchQuery(
            ReleaseSearchMode.Movie,
            "Interstellar",
            2014,
            [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "157336"), new MediaIdentifier(MediaIdentifierScheme.Imdb, "tt0816692")],
            ["2000", "2040"],
            50);

        //Act
        await Search.SearchAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            $"http://prowlarr.test:9696/1/api?t=movie&apikey={ApiKey}&q=Interstellar&imdbid=0816692&tmdbid=157336&year=2014&cat=2000,2040&limit=50",
            request.Uri.AbsoluteUri);
        Assert.Equal(["application/rss+xml", "application/xml", "text/xml"], request.HeaderValues("Accept"));
    }

    [Fact]
    public async Task Search_sends_a_general_search_with_only_the_parameters_it_has()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(TorznabSamples.Feed());

        //Act
        await Search.SearchAsync(Settings, Query(text: "Interstellar 2014"), TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal($"http://prowlarr.test:9696/1/api?t=search&apikey={ApiKey}&q=Interstellar%202014&limit=100", request.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task Search_by_an_identifier_alone_sends_no_text()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(TorznabSamples.Feed());
        var query = Query(ReleaseSearchMode.Movie, text: null, identifiers: [new MediaIdentifier(MediaIdentifierScheme.Tmdb, "157336")], limit: 20);

        //Act
        await Search.SearchAsync(Settings with { ApiKey = null }, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal("http://prowlarr.test:9696/1/api?t=movie&tmdbid=157336&limit=20", request.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("tt0816692", "0816692")]
    // Newznab's imdbid has no prefix in any letter case, and its leading zeros stay (Prowlarr issue #1631).
    [InlineData("TT0000001", "0000001")]
    [InlineData("tt12345678", "12345678")]
    public async Task Search_sends_an_imdb_id_without_its_prefix(string imdbId, string expectedParameter)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(TorznabSamples.Feed());
        var query = Query(ReleaseSearchMode.Movie, text: null, identifiers: [new MediaIdentifier(MediaIdentifierScheme.Imdb, imdbId)]);

        //Act
        await Search.SearchAsync(Settings, query, TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal([expectedParameter], Parameter(request.Uri, "imdbid"));
    }

    [Fact]
    public async Task Search_escapes_every_value_so_none_can_add_a_parameter_or_end_the_query()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(TorznabSamples.Feed());
        var settings = Settings with { ApiKey = "key+/=&?%~!12" };

        //Act
        await Search.SearchAsync(settings, Query(text: "a&apikey=x#"), TestContext.Current.CancellationToken);

        //Assert
        var request = Assert.Single(_harness.TorznabHttp.Requests);
        Assert.Equal("?t=search&apikey=key%2B%2F%3D%26%3F%25~%2112&q=a%26apikey%3Dx%23&limit=100", request.Uri.Query);
        Assert.Equal(["key+/=&?%~!12"], Parameter(request.Uri, "apikey"));
        Assert.Equal(["a&apikey=x#"], Parameter(request.Uri, "q"));
        Assert.Equal("", request.Uri.Fragment);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task Search_refuses_a_query_no_core_code_builds_without_sending_anything(string mistake)
    {
        //Arrange
        var query = MistakenQueries[mistake];

        //Act
        var exception = await Record.ExceptionAsync(() => Search.SearchAsync(Settings, query, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Empty(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Search_refuses_a_missing_query_without_sending_anything()
    {
        //Arrange

        //Act
        var exception = await Record.ExceptionAsync(() => Search.SearchAsync(Settings, null!, TestContext.Current.CancellationToken));

        //Assert
        Assert.IsType<ArgumentNullException>(exception);
        Assert.Empty(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Search_of_the_sample_in_the_movie_categories_leaves_the_audio_release_out()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Search(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(categories: MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        // Toloka's text search finds every section (ADR 0004); the feed doesn't honour cat=, the
        // adapter does.
        Assert.Equal(TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount, hits.Count);
        Assert.DoesNotContain(hits, hit => hit.CategoryIds.Contains(TorznabSamples.NonMovieCategoryId));
        Assert.DoesNotContain(hits, hit => hit.Title == "Interstellar (2014) [DTS, AC3]");
        // The phone-sized rip is filed under 2000 itself, and stays.
        Assert.Contains(hits, hit => hit.Title == "Interstellar (2014) BDRip [640x360]");
    }

    [Fact]
    public async Task Search_of_the_sample_without_categories_lists_every_release_in_the_feeds_order()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Search(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hits = result.Value!;
        Assert.Equal(TorznabSamples.ItemCount, hits.Count);
        Assert.Equal(TorznabSamples.DistinctTitleCount, hits.Select(hit => hit.Title).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", hits[0].Title);
        Assert.Equal("Interstellar (2014) BDRip", hits[^1].Title);
    }

    [Fact]
    public async Task Search_reads_every_field_of_the_first_release_of_the_sample()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Search(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(categories: MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        var hit = result.Value![0];
        Assert.Equal("https://toloka.to/t699400", hit.SourceItemId);
        Assert.Equal("Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", hit.Title);
        // MAM-33 — Toloka's own topic title, which Prowlarr's title cuts to its English part.
        Assert.Equal("Інтерстеллар / Interstellar (2014) BDRemux 1080p Ukr/Eng | Sub Eng [IMAX Edition]", hit.Description);
        Assert.Equal(36711232962, hit.SizeBytes);
        Assert.Equal(8, hit.Seeders);
        Assert.Equal(8, hit.Peers);
        // "Mon, 03 Aug 2026 00:00:00 +0300".
        Assert.Equal(new DateTime(2026, 8, 2, 21, 0, 0, DateTimeKind.Utc), hit.PublishedUtc);
        Assert.Equal(DateTimeKind.Utc, hit.PublishedUtc!.Value.Kind);
        Assert.Equal("https://toloka.to/t699400", hit.SourcePageUrl);
        Assert.Equal(["2040", "100096"], hit.CategoryIds);
        // Prowlarr calls the enclosure application/x-bittorrent whatever the link turns into.
        Assert.Null(hit.PayloadKind);
        Assert.Null(hit.InfoHash);
        Assert.Equal(
            "http://prowlarr.test:9696/1/download?apikey=%7Bapi-key%7D&link=REDACTED&file=Interstellar+(2014)+BDRemux+1080p+Ukr%2FEng+%7C+Sub+Eng+%5BIMAX+Edition%5D",
            hit.Locator.Value);
    }

    [Fact]
    public async Task Search_gives_both_samples_the_same_ids_and_page_links_without_the_session()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The real t=search answer to the general search and the real t=movie answer to the movie search.
        _harness.TorznabHttp.Handler = (request, _) => RssXml(request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal)
            ? TorznabSamples.MovieSearch(BaseUrl, ApiKey)
            : TorznabSamples.Search(BaseUrl, ApiKey));

        //Act
        var search = await Search.SearchAsync(Settings, Query(), cancellationToken);
        var movieSearch = await Search.SearchAsync(Settings, Query(ReleaseSearchMode.Movie), cancellationToken);

        //Assert
        // ADR 0004 — every t=search link carried sid=, none of the t=movie ones did: one release is one
        // id and one link either way, and the stored text never holds the owner's forum session.
        Assert.Equal(TorznabSamples.ItemCount, search.Value!.Count);
        Assert.Equal(movieSearch.Value!.Select(hit => (hit.SourceItemId, hit.SourcePageUrl)), search.Value.Select(hit => (hit.SourceItemId, hit.SourcePageUrl)));
        Assert.All(search.Value, hit =>
        {
            Assert.StartsWith("https://toloka.to/t", hit.SourcePageUrl, StringComparison.Ordinal);
            Assert.Equal(hit.SourcePageUrl, hit.SourceItemId);
            Assert.DoesNotContain("sid", hit.SourcePageUrl, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("?", hit.SourcePageUrl, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Search_keeps_each_download_link_whole_but_for_the_key()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Search(BaseUrl, ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        // MAM-10 — a stored release holds no copy of the gateway's key (ADR 0007); the path, link= and
        // file= are the release's own and stay as the feed wrote them.
        Assert.All(result.Value!, hit =>
        {
            Assert.StartsWith("http://prowlarr.test:9696/1/download?apikey=%7Bapi-key%7D&link=REDACTED&file=Interstellar+(2014)", hit.Locator.Value, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, hit.Locator.Value, StringComparison.Ordinal);
            Assert.Null(hit.PayloadKind);
        });
    }

    [Fact]
    public async Task Search_replaces_the_key_in_any_form_and_leaves_every_other_character()
    {
        //Arrange
        var settings = Settings with { ApiKey = "key+/=&?%~!12" };
        // Escaped, form-encoded with + for a space would be another key; the raw form can't be in a
        // query. Separated by & and ;, a fragment after.
        const string link = "http://prowlarr.test:9696/1/download?apikey=key%2B%2F%3D%26%3F%25~%2112&link=a+b;k=key%2b%2f%3d%26%3f%25%7e%2112&file=x%2Fy#apikey=1";
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item("Interstellar (2014)", link)));

        //Act
        var result = await Search.SearchAsync(settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            "http://prowlarr.test:9696/1/download?apikey=%7Bapi-key%7D&link=a+b;k=%7Bapi-key%7D&file=x%2Fy#apikey=1",
            Assert.Single(result.Value!).Locator.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(100)]
    public async Task Search_returns_at_most_the_limit_in_the_feeds_order(int limit)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Search(BaseUrl, ApiKey));

        //Act
        var all = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);
        var result = await Search.SearchAsync(Settings, Query(limit: limit), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(all.Value!.Take(limit).Select(hit => hit.Locator.Value), result.Value!.Select(hit => hit.Locator.Value));
    }

    [Theory]
    // 6000 UTF-8 bytes is the longest locator kept: sealed, it fits the release's column (8192).
    [InlineData(6000, "x", true)]
    [InlineData(6001, "x", false)]
    // Bytes, not characters: 2970 Cyrillic letters and the link's own 42 are 5982 bytes, 2980 are 6002.
    [InlineData(2970, "ї", true)]
    [InlineData(2980, "ї", false)]
    public async Task Search_keeps_a_download_link_up_to_six_thousand_utf8_bytes(int length, string letter, bool expectedKept)
    {
        //Arrange
        const string prefix = "http://prowlarr.test:9696/1/download?file=";
        var file = string.Concat(Enumerable.Repeat(letter, letter == "x" ? length - prefix.Length : length));
        var link = prefix + file;
        _harness.TorznabHttp.Handler = (_, _) => Xml(TorznabSamples.Feed(TorznabSamples.Item("Interstellar (2014)", link)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedKept, result.Value!.Count == 1);
        Assert.Equal(expectedKept, System.Text.Encoding.UTF8.GetByteCount(link) <= 6000);
    }

    [Fact]
    public async Task Search_skips_each_release_it_can_not_use_and_keeps_the_rest()
    {
        //Arrange
        var feed = TorznabSamples.Feed(
            TorznabSamples.Item(null, DownloadLink),
            TorznabSamples.Item(" ​ ", DownloadLink),
            TorznabSamples.Item("No link", null),
            TorznabSamples.Item("Blank link", "  "),
            TorznabSamples.Item("First good", DownloadLink),
            TorznabSamples.Item("Relative link", "/1/download?link=x"),
            TorznabSamples.Item("Another scheme", "ftp://prowlarr.test:9696/1/download"),
            TorznabSamples.Item("User info", "http://admin:hunter2@prowlarr.test:9696/1/download"),
            // A tab inside the link: XML keeps it in element text, and it is a control character.
            TorznabSamples.Item("Control character", "http://prowlarr.test:9696/1/download?link=a\tb"),
            TorznabSamples.Item("Magnet without a hash", "magnet:?dn=Interstellar"),
            TorznabSamples.Item("Magnet of another kind", "magnet:?xt=urn:sha1:YNCKHTQCWBTRNJIV4WNAE52SJUQCZO5C"),
            TorznabSamples.Item("Too long to seal", $"http://prowlarr.test:9696/1/download?file={new string('x', 6000)}"),
            TorznabSamples.Item("Not parsed as a link", "http://prowlarr.test:9696:9697/1/download"),
            TorznabSamples.Item("Second good", null, extra: $"""<enclosure url="{Escape(DownloadLink)}" length="1" type="application/x-bittorrent" />"""));
        _harness.TorznabHttp.Handler = (_, _) => RssXml(feed);

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(["First good", "Second good"], result.Value!.Select(hit => hit.Title));
    }

    [Fact]
    public async Task Search_takes_the_enclosure_first_then_the_link_then_the_magnet()
    {
        //Arrange
        const string magnet = "magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&dn=Interstellar";
        var magnetAttribute = $"""<torznab:attr name="magneturl" value="{Escape(magnet)}" />""";
        var feed = TorznabSamples.Feed(
            TorznabSamples.Item("Enclosure", "http://prowlarr.test:9696/1/download?from=link", extra: """<enclosure url="http://prowlarr.test:9696/1/download?from=enclosure" />""" + magnetAttribute),
            TorznabSamples.Item("Link", "http://prowlarr.test:9696/1/download?from=link", extra: """<enclosure url=" " />""" + magnetAttribute),
            TorznabSamples.Item("Magnet", null, extra: magnetAttribute));
        _harness.TorznabHttp.Handler = (_, _) => RssXml(feed);

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            [
                ("http://prowlarr.test:9696/1/download?from=enclosure", (ReleasePayloadKind?)null),
                ("http://prowlarr.test:9696/1/download?from=link", null),
                // A magnet is the payload itself: its kind is known, and it is kept as written.
                (magnet, ReleasePayloadKind.Magnet),
            ],
            result.Value!.Select(hit => (hit.Locator.Value, hit.PayloadKind)));
    }

    [Theory]
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567", "0123456789abcdef0123456789abcdef01234567")]
    [InlineData(" 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef ", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456", null)]
    [InlineData("0123456789abcdef0123456789abcdef0123456g", null)]
    [InlineData("", null)]
    public async Task Search_reads_a_hex_info_hash_of_either_version(string infoHash, string? expectedInfoHash)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: $"""<torznab:attr name="infohash" value="{infoHash}" />""")));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedInfoHash, Assert.Single(result.Value!).InfoHash);
    }

    [Theory]
    [InlineData("Mon, 03 Aug 2026 00:00:00 +0300", "2026-08-02T21:00:00Z")]
    // A wrong weekday says nothing the date doesn't; it is dropped before the date is read.
    [InlineData("Fri, 03 Aug 2026 00:00:00 +0300", "2026-08-02T21:00:00Z")]
    [InlineData("  Monday , 3 Aug 2026 12:30:00 GMT ", "2026-08-03T12:30:00Z")]
    [InlineData("03 Aug 2026 00:00:00 -0130", "2026-08-03T01:30:00Z")]
    // No zone: UTC.
    [InlineData("03 Aug 2026 00:00:00", "2026-08-03T00:00:00Z")]
    [InlineData("yesterday", null)]
    [InlineData("", null)]
    public async Task Search_reads_the_publish_date_in_utc_and_keeps_a_release_with_none(string pubDate, string? expectedUtc)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: $"<pubDate>{pubDate}</pubDate>")));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(expectedUtc, hit.PublishedUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("<size>1000</size><enclosure length=\"2000\" /><torznab:attr name=\"size\" value=\"3000\" />", 1000L)]
    [InlineData("<size> </size><enclosure length=\"2000\" /><torznab:attr name=\"size\" value=\"3000\" />", 2000L)]
    [InlineData("<size>-1</size><enclosure length=\"1e3\" /><torznab:attr name=\"size\" value=\"3000\" />", 3000L)]
    [InlineData("<size>1,000</size>", null)]
    [InlineData("<size>99999999999999999999</size>", null)]
    [InlineData("", null)]
    public async Task Search_reads_the_first_size_that_is_a_whole_number_of_bytes(string elements, long? expectedSize)
    {
        //Arrange
        // The enclosure carries no url here, so the release's link is the <link>.
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: elements)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedSize, Assert.Single(result.Value!).SizeBytes);
    }

    [Fact]
    public async Task Search_reads_attributes_in_any_namespace_and_letter_case()
    {
        //Arrange
        const string attributes = """
                                  <newznab:attr xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/" name="SEEDERS" value="12" />
                                  <attr name=" Peers " value="30" />
                                  <torznab:attr name="seeders" value="99" />
                                  <torznab:attr name="category" value="2045" />
                                  <torznab:attr name="category" value="2000" />
                                  """;
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: "<category>2000</category><category>Movies</category>" + attributes)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        var hit = Assert.Single(result.Value!);
        // The first of each name counts.
        Assert.Equal(12, hit.Seeders);
        Assert.Equal(30, hit.Peers);
        // Ids only, each once, in the order listed: elements first, then attributes.
        Assert.Equal(["2000", "2045"], hit.CategoryIds);
    }

    [Theory]
    [InlineData("-1", null)]
    [InlineData("+5", null)]
    [InlineData("5.0", null)]
    [InlineData("lots", null)]
    [InlineData(" 7 ", 7)]
    public async Task Search_reads_a_count_of_zero_or_more_and_nothing_else(string seeders, int? expectedSeeders)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: $"""<torznab:attr name="seeders" value="{seeders}" />""")));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedSeeders, Assert.Single(result.Value!).Seeders);
    }

    [Theory]
    // A parent asked for keeps its whole thousand: both gateways expand it so.
    [InlineData("2045", new[] { "2000" }, true)]
    [InlineData("2045", new[] { "2040" }, false)]
    [InlineData("2040", new[] { "2040" }, true)]
    [InlineData("100096", new[] { "2000", "2040" }, false)]
    [InlineData("100096", new[] { "100000" }, true)]
    [InlineData("3020", new[] { "2000" }, false)]
    // Listed in no category: the feed filtered it, or nothing can be said.
    [InlineData(null, new[] { "2000" }, true)]
    // Nothing asked: everything stays.
    [InlineData("3020", new string[0], true)]
    public async Task Search_keeps_a_release_only_in_a_category_asked_for(string? category, string[] asked, bool expectedKept)
    {
        //Arrange
        var extra = category is null ? "" : $"<category>{category}</category>";
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: extra)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(categories: asked), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedKept, result.Value!.Count == 1);
    }

    [Fact]
    public async Task Search_masks_the_api_key_a_title_echoes()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item($"Interstellar {ApiKey}\n(2014)", DownloadLink)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Interstellar *** (2014)", Assert.Single(result.Value!).Title);
    }

    [Fact]
    public async Task Search_cuts_a_title_to_what_a_release_holds()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item(new string('t', 1500), DownloadLink)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(new string('t', 999) + "…", Assert.Single(result.Value!).Title);
    }

    [Theory]
    [InlineData("Інтерстеллар / Interstellar (2014) BDRip", "Інтерстеллар / Interstellar (2014) BDRip")]
    // Cleaned like the title: no control or format character, every run of whitespace one space.
    [InlineData("  Інтерстеллар‮ /\n\tInterstellar  (2014) ", "Інтерстеллар / Interstellar (2014)")]
    // MAM-33 — nothing more than the title says: none, cleaned or not.
    [InlineData("Interstellar (2014)", null)]
    [InlineData(" Interstellar\n(2014) ", null)]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task Search_reads_the_description_only_when_it_says_more_than_the_title(string? description, string? expectedDescription)
    {
        //Arrange
        var extra = description is null ? "" : $"<description>{Escape(description)}</description>";
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: extra)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedDescription, Assert.Single(result.Value!).Description);
    }

    [Fact]
    public async Task Search_masks_the_api_key_a_description_echoes()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: $"<description>Інтерстеллар {ApiKey} / Interstellar (2014)</description>")));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("Інтерстеллар *** / Interstellar (2014)", Assert.Single(result.Value!).Description);
    }

    [Fact]
    public async Task Search_cuts_a_description_to_what_a_release_holds()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", DownloadLink, extra: $"<description>{new string('d', 1500)}</description>")));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        // ReleaseEntity.Description holds 1000, as the title's column does.
        Assert.Equal(new string('d', 999) + "…", Assert.Single(result.Value!).Description);
    }

    [Fact]
    public async Task Search_fails_when_the_feed_lists_its_downloads_at_another_address()
    {
        //Arrange
        // Prowlarr builds its links from the request's Host header, a reverse proxy or a base-URL
        // override: here the owner entered one name and Prowlarr links to another.
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Search("http://10.0.0.5:9696/1/api", ApiKey));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(
            "The Torznab feed lists its downloads at http://10.0.0.5:9696, not at the address entered for it, so the app couldn't fetch them. Enter the feed at the address its download links use.",
            TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Search_names_only_the_origin_of_a_link_elsewhere()
    {
        //Arrange
        const string link = "https://tracker.test:8443/dl/planted-passkey-0001/x.torrent?passkey=planted-passkey-0002";
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Here", DownloadLink),
            TorznabSamples.Item("Elsewhere", link)));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.StartsWith("The Torznab feed lists its downloads at https://tracker.test:8443, not at", TestTexts.En(result.Error), StringComparison.Ordinal);
        Assert.DoesNotContain("planted", TestTexts.En(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_of_the_same_origin_written_differently_succeeds()
    {
        //Arrange
        // Another letter case, an explicit default port and a trailing dot are still the feed's origin.
        var settings = Settings with { BaseUrl = "http://Prowlarr.test/1/api" };
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(
            TorznabSamples.Item("Interstellar (2014)", "http://prowlarr.TEST.:80/1/download?link=x")));

        //Act
        var result = await Search.SearchAsync(settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Search_of_an_empty_feed_lists_nothing()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed());

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Empty(result.Value!);
    }

    [Theory]
    [InlineData("100", 401, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (error 100: Incorrect parameter). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData("201", 200, IntegrationCallStatus.UnexpectedResponse, "The Torznab feed didn't understand the search (error 201: Incorrect parameter).")]
    [InlineData("203", 400, IntegrationCallStatus.UnexpectedResponse, "The Torznab feed didn't understand the search (error 203: Incorrect parameter).")]
    [InlineData("429", 200, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (error 429: Incorrect parameter). Try again in a few minutes.")]
    [InlineData("910", 503, IntegrationCallStatus.Refused, "The Torznab feed has its API switched off (error 910: Incorrect parameter).")]
    [InlineData("900", 500, IntegrationCallStatus.ServiceError, "The Torznab feed failed (error 900: Incorrect parameter).")]
    [InlineData("410", 410, IntegrationCallStatus.ServiceError, "The Torznab feed failed (error 410: Incorrect parameter).")]
    [InlineData("none", 200, IntegrationCallStatus.UnexpectedResponse, NotSearchResultsError)]
    public async Task Search_maps_each_torznab_error_code_with_the_searchs_words(
        string code, int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml($"""<error code="{code}" description="Incorrect parameter" />""", (HttpStatusCode)statusCode);

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Search_masks_the_key_and_cuts_the_addresses_an_error_description_quotes()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (request, _) => Xml(
            $"""<error code="900" description="Search of {Escape(request.RequestUri!.AbsoluteUri)} failed for key {ApiKey}" />""");

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal("The Torznab feed failed (error 900: Search of http://prowlarr.test:9696 failed for key ***).", TestTexts.En(result.Error));
    }

    [Theory]
    [InlineData(401, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData(403, IntegrationCallStatus.AuthenticationFailed, "The Torznab feed refused the API key (403 Forbidden). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.")]
    [InlineData(404, IntegrationCallStatus.UnexpectedResponse,
        "Nothing answers Torznab at this address (404 Not Found). Use the feed's Torznab address: Prowlarr's ends in /api, and Jackett's Copy Torznab Feed gives one ending in /results/torznab/.")]
    [InlineData(429, IntegrationCallStatus.Refused,
        "The Torznab feed is refusing requests for now (429 Too Many Requests). Try again in a few minutes.")]
    [InlineData(502, IntegrationCallStatus.ServiceError, "Torznab answered 502 Bad Gateway. Its own log says why.")]
    [InlineData(200, IntegrationCallStatus.UnexpectedResponse, NotSearchResultsError)]
    public async Task Search_of_an_html_answer_says_why_with_its_status(int statusCode, IntegrationCallStatus expectedStatus, string expectedMessage)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) =>
            VendorResponses.Text("<!DOCTYPE html><html><body>Error</body></html>", (HttpStatusCode)statusCode, "text/html");

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedMessage, TestTexts.En(result.Error));
    }

    [Theory]
    [InlineData("<caps><searching /></caps>")]
    [InlineData("<rss version=\"2.0\" />")]
    [InlineData("""{"items":[]}""")]
    public async Task Search_of_something_other_than_search_results_is_an_unexpected_response(string body)
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => Xml(body);

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotSearchResultsError, TestTexts.En(result.Error));
    }

    [Theory]
    // Deep nesting inside a title (read as text, which recurses once per level) and where nothing reads
    // it: either way the answer is refused before it is built, never a crash or minutes of parsing.
    [InlineData("<rss><channel><item><title>{0}</title></item></channel></rss>")]
    [InlineData("<rss><channel><junk>{0}</junk></channel></rss>")]
    public async Task Search_of_an_answer_nested_too_deep_is_refused_in_moments(string shape)
    {
        //Arrange
        const int levels = 40_000;
        var nested = string.Concat(Enumerable.Repeat("<a>", levels)) + string.Concat(Enumerable.Repeat("</a>", levels));
        var body = string.Format(CultureInfo.InvariantCulture, shape, nested);
        _harness.TorznabHttp.Handler = (_, _) => Xml(body);
        var started = TimeProvider.System.GetTimestamp();

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(NotSearchResultsError, TestTexts.En(result.Error));
        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(5), "Reading a deeply nested answer took seconds.");
    }

    [Fact]
    public async Task Search_reads_an_answer_nested_as_deep_as_a_feed_may_go()
    {
        //Arrange
        // Thirty levels inside an item's extension element: deeper than any Torznab answer, within the cap.
        var nested = string.Concat(Enumerable.Repeat("<a>", 28)) + string.Concat(Enumerable.Repeat("</a>", 28));
        _harness.TorznabHttp.Handler = (_, _) => RssXml(TorznabSamples.Feed(TorznabSamples.Item("Interstellar (2014)", DownloadLink) + $"<extra>{nested}</extra>"));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task Search_reports_a_redirect_and_sends_one_request()
    {
        //Arrange
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Redirect($"http://prowlarr.test:9696/login?returnUrl=%2F1%2Fapi%3Fapikey%3D{ApiKey}");

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, result.Status);
        Assert.Equal(
            "The address answered with a redirect to http://prowlarr.test:9696/login. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.",
            TestTexts.En(result.Error));
        Assert.Single(_harness.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Search_through_the_fake_feed_reads_its_releases()
    {
        //Arrange
        var feed = new FakeTorznabServer(BaseUrl, ApiKey);
        feed.Arm(_harness.TorznabHttp);

        //Act
        var result = await Search.SearchAsync(Settings, Query(ReleaseSearchMode.Movie, categories: MovieCategories), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        Assert.Equal(TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount, result.Value!.Count);
    }

    [Fact]
    public async Task Search_of_prowlarrs_recorded_answer_to_a_wrong_key_says_the_key_was_refused()
    {
        //Arrange
        // Prowlarr's real answer to a wrong key: a bare 401, no body, not Torznab's <error code="100">.
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, result.Status);
        Assert.Equal("The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.", TestTexts.En(result.Error));
    }

    private static ReleaseSearchQuery Query(
        ReleaseSearchMode mode = ReleaseSearchMode.General,
        string? text = "Interstellar",
        IReadOnlyList<MediaIdentifier>? identifiers = null,
        IReadOnlyList<string>? categories = null,
        int limit = 100) =>
        new(mode, text, null, identifiers ?? [], categories ?? [], limit);

    /// <summary>Every value of <paramref name="name"/> in the request's query, decoded.</summary>
    private static string[] Parameter(Uri uri, string name) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => string.Equals(Uri.UnescapeDataString(pair[0]), name, StringComparison.Ordinal))
            .Select(pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "")
            .ToArray();

    private static string Escape(string value) => SecurityElement.Escape(value);

    private static HttpResponseMessage Xml(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        VendorResponses.Text(body, status, "application/xml");

    private static HttpResponseMessage RssXml(string body) => VendorResponses.Text(body, HttpStatusCode.OK, "application/rss+xml");
}
