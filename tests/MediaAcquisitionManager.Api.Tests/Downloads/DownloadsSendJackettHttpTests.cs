using System.Net;
using System.Net.Http.Json;
using System.Security;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30 — a release Jackett listed, sent end to end (Vasyl's decision 5): fetched once from Jackett's download link at
// its host's root with the key put back in jackett_apikey, and Jackett's recorded refusals — a wrong key's bare 401, a
// download token that doesn't unprotect a bare 404 — ending the job with the feed's sentence, the refused key's with
// where to copy the right one (D16), before any client is asked. The key and the token show nowhere: not in an answer,
// a log line, the job's row or an audit row, even when Jackett's failure quotes them (ADR 0004 — Jackett's path= is a
// protected link like Prowlarr's link=, which DownloadsSecretsHttpTests sweeps).
public sealed class DownloadsSendJackettHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0001";

    /// <summary>The v1 info hash of <see cref="TorznabSamples.TorrentFile"/>, which the feed hands over.</summary>
    private const string SampleInfoHash = "72d6069eff259a0800f7afdb2500d5de591a9f01";

    private const string RefusedKeyError =
        "The Torznab feed refused the API key (401 Unauthorized). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.";

    private const string DownloadNotFoundError =
        "The Torznab feed couldn't hand over this release (404 Not Found): it may be gone, or the feed failed to fetch it; its own log says which.";

    private const string RefusedKey = "a-key-jackett-refuses";

    private const string MangledPathToken = "mangled-path-token-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private FakeTorznabServer _jackett = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;

    public DownloadsSendJackettHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        // The owner sends: a failure's sentence is the owner's to read (the MAM-8 rule).
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jackett = FakeTorznabServer.Jackett(JackettAddress, ApiKey);
        _jackett.Arm(_factory.TorznabHttp);
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

    [Fact]
    public async Task Create_of_a_release_jackett_listed_fetches_it_from_jacketts_download_link_with_the_key_put_back_and_sends_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        var release = await _releases.CreateFound(_owner.User, jackett, locator: TorznabSamples.JackettLocator(JackettAddress));

        //Act
        var response = await SendAsync(release);

        //Assert
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Submitted, SampleInfoHash, (string?)null), (job.Status, job.ClientJobId, job.Error));
        // Once, at the host's root and not under the feed's address, with the key and the token as Jackett needs them.
        var fetch = Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Equal(
            $"http://jackett.test:9117/dl/toloka/?jackett_apikey={ApiKey}&path={TorznabSamples.JackettPathToken}&file=Interstellar%20%282014%29%20BDRip%201080p",
            fetch.Uri.AbsoluteUri);
        Assert.Equal(TorznabSamples.TorrentFile(), Assert.Single(_qbittorrent.AddRequests).TorrentFile);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TorznabSamples.JackettPathToken, body, StringComparison.Ordinal);
        await AssertNoLogJobOrAuditRowCarriesAsync([ApiKey, TorznabSamples.JackettPathToken], cancellationToken);
    }

    [Fact]
    public async Task Create_whose_key_jackett_refuses_fails_with_where_to_copy_the_right_one_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: RefusedKey);
        var release = await _releases.CreateFound(_owner.User, jackett, locator: TorznabSamples.JackettLocator(JackettAddress));

        //Act
        var response = await SendAsync(release);

        //Assert
        // Jackett's recorded answer to a download link with a wrong jackett_apikey: a bare 401.
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, (string?)null, RefusedKeyError), (job.Status, job.ClientJobId, job.Error));
        Assert.Single(_factory.TorznabHttp.Requests);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal((DownloadJobStatus.Failed, RefusedKeyError), (row.Status, row.Error));
            Assert.Null(row.InfoHashV1);
        });
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(RefusedKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TorznabSamples.JackettPathToken, body, StringComparison.Ordinal);
        await AssertNoLogJobOrAuditRowCarriesAsync([RefusedKey, ApiKey, TorznabSamples.JackettPathToken], cancellationToken);
    }

    [Fact]
    public async Task Create_whose_download_token_jackett_does_not_unprotect_fails_with_the_feeds_sentence_and_asks_no_client()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        var locator = TorznabSamples.JackettLocator(JackettAddress).Replace(TorznabSamples.JackettPathToken, MangledPathToken, StringComparison.Ordinal);
        var release = await _releases.CreateFound(_owner.User, jackett, locator: locator);

        //Act
        var response = await SendAsync(release);

        //Assert
        // Jackett's recorded answer to a token that doesn't unprotect: a bare 404, as to every failed download.
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal((DownloadJobStatus.Failed, DownloadNotFoundError), (job.Status, job.Error));
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        await AssertNoLogJobOrAuditRowCarriesAsync([ApiKey, MangledPathToken], cancellationToken);
    }

    [Fact]
    public async Task Create_whose_download_jackett_fails_quoting_its_key_and_token_keeps_both_out_of_the_answer_the_rows_and_the_log()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jackett = await _integrations.Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        var release = await _releases.CreateFound(_owner.User, jackett, locator: TorznabSamples.JackettLocator(JackettAddress));
        // Jackett's error quotes the link it was asked for (XML-escaped), key and token in its query, and both again in its
        // own words: only the masking of a fetch's secrets by value keeps the token out, as path= names no credential.
        _jackett.Intercept = request => VendorResponses.Text(
            $"""<error code="900" description="Fetching {SecurityElement.Escape(request.RequestUri!.AbsoluteUri)} failed: key {ApiKey}, token {TorznabSamples.JackettPathToken}" />""",
            HttpStatusCode.InternalServerError,
            "application/xml");

        //Act
        var response = await SendAsync(release);

        //Assert
        var job = await ReadJobAsync(response, cancellationToken);
        Assert.Equal(
            (DownloadJobStatus.Failed, "The Torznab feed failed to hand over the release (error 900: Fetching http://jackett.test:9117 failed: key ***, token ***)."),
            (job.Status, job.Error));
        // The planted values were on the wire: what the sweep looks for was there to leak.
        Assert.Contains($"jackett_apikey={ApiKey}&path={TorznabSamples.JackettPathToken}", Assert.Single(_factory.TorznabHttp.Requests).Uri.Query, StringComparison.Ordinal);
        Assert.Empty(_factory.QBittorrentHttp.Requests);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(TorznabSamples.JackettPathToken, body, StringComparison.Ordinal);
        await AssertNoLogJobOrAuditRowCarriesAsync([ApiKey, TorznabSamples.JackettPathToken], cancellationToken);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// That none of <paramref name="secrets"/> is in a log line or its exception, in any text column of the one job's row
    /// or of an audit row — the sweep <c>DownloadsSecretsHttpTests</c> runs over Prowlarr's link= token, for Jackett's
    /// key and path= token. A send logs its outcome, so the log read is never empty.
    /// </summary>
    private async Task AssertNoLogJobOrAuditRowCarriesAsync(string[] secrets, CancellationToken cancellationToken)
    {
        Assert.Contains(_factory.Logs.Entries, entry => entry.Message.StartsWith("Download job ", StringComparison.Ordinal));
        Assert.All(_factory.Logs.Entries, entry => Assert.All(secrets, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.DownloadJobs.AsNoTracking().SingleAsync(cancellationToken);
            var clear = string.Join(
                '\n',
                row.Error,
                row.ClientJobId,
                row.DownloadClientName,
                row.DestinationName,
                row.ClientDestinationKey,
                row.RequestedByUserName,
                row.InfoHashV1,
                row.InfoHashV2,
                row.BlockReason);
            Assert.All(secrets, secret => Assert.DoesNotContain(secret, clear, StringComparison.Ordinal));
            var audits = await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Single(audits, audit => audit.Action == AuditAction.DownloadRequested);
            Assert.All(audits, audit => Assert.All(
                secrets,
                secret => Assert.DoesNotContain(secret, string.Join('\n', audit.ActorUserName, audit.TargetName), StringComparison.Ordinal)));
        });
    }

    // The owner's send of the release to qBittorrent's Movies, under a request id of its own.
    private Task<HttpResponseMessage> SendAsync(ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
