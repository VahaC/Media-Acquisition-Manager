using System.Net;
using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Integrations.Torznab;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-10 — what a listing's page link and id become (TorznabLinks): both are stored in clear and
// printed by the hit's ToString, so the owner's forum session (Toloka's sid, ADR 0004) is removed, and a
// link that names a credential is refused or, for the id, replaced by a digest. Prowlarr writes the
// tracker's own download link, passkey and all, as the guid of an indexer that gives none.
public sealed class TorznabAdapterSearchLinksTests : IDisposable
{
    private const string BaseUrl = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "0f9e8d7c6b5a49382716a5b4c3d2e1f0";
    private const string LinkToken = "Q2ZESjhQcm90ZWN0ZWQrL3Rva2Vu";
    private const string DownloadLink = $"http://prowlarr.test:9696/1/download?apikey={ApiKey}&link={LinkToken}&file=Interstellar";

    private static readonly IntegrationConnectionSettings Settings = new(null, BaseUrl, null, null, ApiKey);

    private readonly IntegrationAdapterHarness _harness = new();

    private IReleaseSearchProvider Search => _harness.Adapter<IReleaseSearchProvider>(TorznabAdapter.TypeId);

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("https://toloka.to/t699400?sid=4f2a9c0e1b7d", "https://toloka.to/t699400")]
    [InlineData("https://toloka.to/t699400?SID=4f2a9c0e1b7d", "https://toloka.to/t699400")]
    [InlineData("https://toloka.to/viewtopic.php?t=699400&sid=4f2a9c0e1b7d", "https://toloka.to/viewtopic.php?t=699400")]
    [InlineData("https://toloka.to/viewtopic.php?t=699400;sid=4f2a9c0e1b7d", "https://toloka.to/viewtopic.php?t=699400")]
    [InlineData("https://toloka.to/viewtopic.php?sid=4f2a9c0e1b7d&t=699400&Sid=5e3b&p=2", "https://toloka.to/viewtopic.php?t=699400&p=2")]
    // The name percent-encoded, as a hostile or sloppy feed may write it.
    [InlineData("https://toloka.to/t699400?s%69d=4f2a9c0e1b7d", "https://toloka.to/t699400")]
    // A fragment is never part of the page, and a session can hide there too.
    [InlineData("https://toloka.to/t699400#sid=4f2a9c0e1b7d", "https://toloka.to/t699400")]
    [InlineData("https://toloka.to/t699400?", "https://toloka.to/t699400")]
    [InlineData("  https://toloka.to/t699400  ", "https://toloka.to/t699400")]
    // Not a session: "sids" and "session" are other names.
    [InlineData("https://toloka.to/t699400?sids=1&session=2", "https://toloka.to/t699400?sids=1&session=2")]
    public async Task Search_removes_the_session_from_a_page_link_and_an_id(string link, string expected)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: link, comments: link));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(expected, hit.SourcePageUrl);
        Assert.Equal(expected, hit.SourceItemId);
    }

    [Fact]
    public async Task Search_of_a_guid_that_is_the_trackers_download_link_stores_a_digest_and_no_page()
    {
        //Arrange
        // Prowlarr's guid when the indexer gave none: the tracker's raw download link, with the passkey
        // in the path and the query.
        const string guid = "https://tracker.test/dl/planted-passkey-0001/x.torrent?passkey=planted-passkey-0002&sid=planted-sid-0001";
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guid));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Null(hit.SourcePageUrl);
        // The digest of the link without its session: stable from one search to the next.
        Assert.Equal(Digest("https://tracker.test/dl/planted-passkey-0001/x.torrent?passkey=planted-passkey-0002"), hit.SourceItemId);
        Assert.Matches("^sha256:[0-9a-f]{64}\\z", hit.SourceItemId);
    }

    [Theory]
    // A tracker's download link with the passkey or the RSS key in its path, not its query: nothing
    // about it names a credential, so only a guid that is the page link itself is kept in clear.
    [InlineData("https://tracker.test/rss/download/123/planted-rsskey-0001/x.torrent")]
    [InlineData("https://tracker.test/torrent/download/123.planted-rsskey-0001")]
    [InlineData("https://toloka.to/t699401")]
    public async Task Search_of_a_web_guid_that_is_not_the_page_link_stores_its_digest(string guid)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guid, comments: "https://toloka.to/t699400"));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal("https://toloka.to/t699400", hit.SourcePageUrl);
        Assert.Equal(Digest(guid), hit.SourceItemId);
    }

    [Theory]
    // Not absolute web links, yet a credential all the same: protocol-relative, relative, scheme-only.
    [InlineData("//tracker.test/download.php?id=1&passkey=planted-passkey-0001")]
    [InlineData("download.php?id=1&passkey=planted-passkey-0001")]
    [InlineData("https:tracker.test/download.php?id=1&passkey=planted-passkey-0001")]
    [InlineData("t699400?sid=planted-sid-0001")]
    public async Task Search_of_a_guid_that_is_a_link_of_another_shape_stores_its_digest(string guid)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guid));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(Digest(guid), hit.SourceItemId);
        Assert.DoesNotContain("planted", hit.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://tracker.test/rss/download/123/planted-rsskey-0001/x.torrent")]
    [InlineData("https://tracker.test/torrent/download/123.planted-rsskey-0001")]
    [InlineData("https://tracker.test/dl/123/planted-rsskey-0001")]
    [InlineData("https://tracker.test/download.php?id=123")]
    [InlineData("https://tracker.test/details/Interstellar.TORRENT")]
    public async Task Search_refuses_a_page_link_that_looks_like_a_download(string comments)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: "toloka-699400", comments: comments));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Null(hit.SourcePageUrl);
    }

    [Fact]
    public async Task Search_skips_a_listing_whose_download_host_has_no_ascii_form_and_keeps_the_rest()
    {
        //Arrange
        // A zero-width joiner in a host: Uri takes it, but its ASCII form can't be made.
        ArmItems(
            TorznabSamples.Item("Joined", "http://a‍b.test/1/download?link=x", comments: "http://a‍b.test/t1"),
            TorznabSamples.Item("Fine", DownloadLink, comments: "http://c‍d.test/t2"));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        Assert.Equal(IntegrationCallStatus.Succeeded, result.Status);
        var hit = Assert.Single(result.Value!);
        Assert.Equal("Fine", hit.Title);
        Assert.Null(hit.SourcePageUrl);
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&tr=http%3A%2F%2Ftracker.test%2Fplanted-passkey-0001%2Fannounce")]
    [InlineData("MAGNET:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567")]
    public async Task Search_of_a_magnet_guid_stores_its_digest(string guid)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guid));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(Digest(guid), hit.SourceItemId);
    }

    [Theory]
    // An opaque id is kept as written.
    [InlineData("8a3f1c2e-0b9d-4e6f-a1b2-c3d4e5f60718", true)]
    [InlineData("urn:uuid:8a3f1c2e-0b9d-4e6f-a1b2-c3d4e5f60718", true)]
    [InlineData("toloka-699400", true)]
    // One that holds the connection's key, an address of another kind, or one too long is not.
    [InlineData($"id-{ApiKey}", false)]
    [InlineData("ftp://user:planted-password-0001@tracker.test/x", false)]
    [InlineData("http://tracker.test:99999999/planted-passkey-0001", false)]
    public async Task Search_keeps_an_opaque_id_unless_it_could_hold_a_credential(string guid, bool expectedKept)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guid));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(expectedKept ? guid : Digest(guid), hit.SourceItemId);
    }

    [Fact]
    public async Task Search_of_an_id_too_long_for_its_column_stores_its_digest()
    {
        //Arrange
        var opaque = new string('i', 513);
        var link = "https://toloka.to/t1?q=" + new string('q', 600);
        ArmItems(
            TorznabSamples.Item("Opaque", DownloadLink, guid: opaque),
            TorznabSamples.Item("Link", DownloadLink, guid: link));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([Digest(opaque), Digest(link)], result.Value!.Select(hit => hit.SourceItemId));
    }

    [Theory]
    // The page link may be up to 2048 characters, the id column holds 512: a longer page link is an id
    // only as its digest, whether it came as the guid too or stood in for a missing one.
    [InlineData(true)]
    [InlineData(false)]
    public async Task Search_of_a_page_link_longer_than_an_id_stores_its_digest_as_the_id(bool guidIsThePageLink)
    {
        //Arrange
        var page = "https://toloka.to/t699400?q=" + new string('q', 600);
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guidIsThePageLink ? page : null, comments: page));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(page, hit.SourcePageUrl);
        Assert.Equal(Digest(page), hit.SourceItemId);
    }

    [Fact]
    public async Task Search_of_an_id_with_a_control_character_stores_its_digest()
    {
        //Arrange
        const string guid = "toloka\t699400";
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: guid));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal(Digest(guid), hit.SourceItemId);
    }

    [Fact]
    public async Task Search_without_a_guid_takes_the_page_link_as_the_id()
    {
        //Arrange
        ArmItems(
            TorznabSamples.Item("With a page", DownloadLink, comments: "https://toloka.to/t699400?sid=4f2a9c0e1b7d"),
            TorznabSamples.Item("Without", DownloadLink, guid: "   "));

        //Act
        var result = await Search.SearchAsync(Settings, Query(), TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(["https://toloka.to/t699400", null], result.Value!.Select(hit => hit.SourceItemId));
        Assert.All(result.Value!, hit => Assert.Equal(hit.SourcePageUrl, hit.SourceItemId));
    }

    [Theory]
    // The feed's own address is a gateway, not a page — and it would show members the owner's LAN.
    [InlineData("http://prowlarr.test:9696/1/details/699400")]
    [InlineData("http://PROWLARR.test.:9696/")]
    // A parameter that names a credential, decoded and in any letter case.
    [InlineData("https://toloka.to/t699400?passkey=9f8e7d6c")]
    [InlineData("https://toloka.to/t699400?PassKey=9f8e7d6c")]
    [InlineData("https://toloka.to/t699400?p%61sskey=9f8e7d6c")]
    [InlineData("https://toloka.to/t699400?a=1;token=9f8e7d6c")]
    [InlineData("https://toloka.to/t699400?uk=1")]
    [InlineData("https://toloka.to/t699400?link=x")]
    // The download link's token under another name.
    [InlineData($"https://toloka.to/t699400?ref={LinkToken}")]
    // Not a web page, or not an absolute one.
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript://toloka.to/%0aalert(1)")]
    [InlineData("ftp://toloka.to/t699400")]
    [InlineData("data:text/html,<p>hi</p>")]
    [InlineData("/t699400")]
    [InlineData("toloka.to/t699400")]
    // User info: a credential by definition.
    [InlineData("https://owner:planted-password-0001@toloka.to/t699400")]
    [InlineData("https://owner@toloka.to/t699400")]
    public async Task Search_refuses_a_page_link_that_could_carry_a_credential(string comments)
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, comments: comments));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Null(hit.SourcePageUrl);
        // No guid: the id is the page link, so it goes too.
        Assert.Null(hit.SourceItemId);
    }

    [Theory]
    [InlineData("key%2B%2F%3D%26%3F%25~%2112")]
    [InlineData("key%2b%2f%3d%26%3f%25%7e%2112")]
    public async Task Search_refuses_a_page_link_that_echoes_the_escaped_api_key(string echoed)
    {
        //Arrange
        var settings = Settings with { ApiKey = "key+/=&?%~!12" };
        ArmItems(TorznabSamples.Item(
            "Interstellar (2014)", "http://prowlarr.test:9696/1/download?link=x", comments: $"https://toloka.to/t699400?ref={echoed}"));

        //Act
        var result = await SearchAsync(settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Null(hit.SourcePageUrl);
    }

    [Fact]
    public async Task Search_refuses_a_page_link_longer_than_its_column()
    {
        //Arrange
        var comments = "https://toloka.to/t699400?q=" + new string('q', 2048);
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, guid: "toloka-699400", comments: comments));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Null(hit.SourcePageUrl);
        Assert.Equal("toloka-699400", hit.SourceItemId);
    }

    [Fact]
    public async Task Search_stores_a_page_link_in_its_normal_form()
    {
        //Arrange
        ArmItems(TorznabSamples.Item("Interstellar (2014)", DownloadLink, comments: "HTTPS://Toloka.TO:443/t699400?q=Interstellar 2014&sid=1"));

        //Act
        var result = await SearchAsync(Settings);

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal("https://toloka.to/t699400?q=Interstellar%202014", hit.SourcePageUrl);
    }

    [Fact]
    public async Task Search_hit_prints_no_planted_credential()
    {
        //Arrange
        string[] planted = ["planted-passkey-0001", "planted-passkey-0002", "planted-sid-0001", ApiKey, LinkToken];
        ArmItems(TorznabSamples.Item(
            "Interstellar (2014)",
            DownloadLink,
            guid: "https://tracker.test/dl/planted-passkey-0001/x.torrent?passkey=planted-passkey-0002",
            comments: "https://toloka.to/t699400?sid=planted-sid-0001"));

        //Act
        var result = await SearchAsync(Settings);
        var printed = string.Join('\n', result.Value!.Select(hit => hit.ToString()));

        //Assert
        var hit = Assert.Single(result.Value!);
        Assert.Equal("https://toloka.to/t699400", hit.SourcePageUrl);
        Assert.All(planted, secret => Assert.DoesNotContain(secret, printed, StringComparison.Ordinal));
        // The stored locator keeps the token (the fetch needs it) and drops the key.
        Assert.Contains(LinkToken, hit.Locator.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, hit.Locator.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void SameOrigin_holds_for_one_origin_however_it_is_written_and_for_nothing_else()
    {
        //Arrange
        var feed = new Uri("http://prowlarr.test:9696/1/api");
        string[] same = ["http://PROWLARR.test:9696/x", "http://prowlarr.test.:9696/x", "HTTP://prowlarr.test:9696"];
        string[] other =
        [
            "https://prowlarr.test:9696/x", "http://prowlarr.test:9697/x", "http://prowlarr.test/x", "http://other.test:9696/x",
            "http://user@prowlarr.test:9696/x", "file://prowlarr.test/x",
        ];

        //Act
        var sameAnswers = same.Select(link => TorznabLinks.SameOrigin(new Uri(link), feed)).ToList();
        var otherAnswers = other.Select(link => TorznabLinks.SameOrigin(new Uri(link), feed)).ToList();
        var dataAnswer = TorznabLinks.SameOrigin(new Uri("data:text/plain,a"), new Uri("data:text/plain,b"));

        //Assert
        Assert.All(sameAnswers, Assert.True);
        Assert.All(otherAnswers, Assert.False);
        // Two addresses without a host are no origin at all.
        Assert.False(dataAnswer);
    }

    private void ArmItems(params string[] items)
    {
        var feed = TorznabSamples.Feed(items);
        _harness.TorznabHttp.Handler = (_, _) => VendorResponses.Text(feed, HttpStatusCode.OK, "application/rss+xml");
    }

    private Task<IntegrationCallResult<IReadOnlyList<ReleaseSearchHit>>> SearchAsync(IntegrationConnectionSettings settings) =>
        Search.SearchAsync(settings, Query(), TestContext.Current.CancellationToken);

    private static ReleaseSearchQuery Query() => new(ReleaseSearchMode.General, "Interstellar", null, [], [], 100);

    private static string Digest(string text) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
