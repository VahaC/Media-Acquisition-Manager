using System.Net;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-13 — the ticket's "no secret appears in any response" for sending a release: every credential a send
// handles — the feed's API key and its link= token (the release's download link), qBittorrent's password
// and the sessions it hands out, a magnet's tracker passkey — is planted where the services put it; the
// sends that succeed and the failures that echo them (a feed error quoting the link, a qBittorrent answer
// quoting its session and password, a redirect to the tracker's address, a 500 whose body repeats them)
// are driven by the owner and a member; and none of them, nor a stored ciphertext, comes back in an answer,
// reaches the log, or is stored on a job or an audit row (ADR 0004, ADR 0007). The discovery guard keeps
// the sweep whole: a download endpoint added later shows up there and needs a sweep of its own.
public sealed class DownloadsSecretsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    // Spelled out rather than read from the controller: this pins where download controllers live.
    private const string DownloadsNamespace = "MediaAcquisitionManager.Api.Controllers.Downloads";

    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";

    // Distinctive, URL-safe and 8 characters or more, so masking never skips one and the raw and escaped
    // forms are the same text.
    private const string ApiKey = "planted-torznab-key-0001";
    private const string LinkToken = ReleaseSeeder.PlantedLinkToken;
    private const string QBittorrentPassword = "planted-qbt-password-0001";
    private const string MagnetPasskey = "planted-passkey-0001";
    private const string EchoedMagnetPasskey = "planted-passkey-0002";

    /// <summary>The tracker of the magnet whose add is answered with a redirect to it.</summary>
    private const string EchoedTrackerAddress = $"http://tracker.test/announce/{EchoedMagnetPasskey}";

    private static readonly string[] PlantedValues = [ApiKey, LinkToken, QBittorrentPassword, MagnetPasskey, EchoedMagnetPasskey];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeQBittorrentServer _qbittorrent = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _destination = null!;

    public DownloadsSecretsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey);
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _qbittorrent = new FakeQBittorrentServer { Password = QBittorrentPassword };
        _qbittorrent.Arm(_factory.QBittorrentHttp);
        _client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            _qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: QBittorrentPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _destination = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Movies", "Movies", isDefault: true);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_download_endpoint_of_today()
    {
        //Arrange

        //Act
        var endpoints = DownloadEndpoints()
            .Select(endpoint => $"{string.Join(',', endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}")
            .Order(StringComparer.Ordinal);

        //Assert
        // Guards the sweep: these are the endpoints read below. One added later shows up here and needs
        // reading there too.
        Assert.Equal(["GET api/downloads/options", "GET api/downloads/{id:guid}", "POST api/downloads"], endpoints);
    }

    [Fact]
    public async Task No_answer_log_job_or_audit_row_of_a_download_carries_a_planted_secret()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var torrent = await _releases.CreateFound(_owner.User, _toloka, "Interstellar (2014) BDRip 1080p");
        var magnet = await _releases.CreateFound(
            _member.User, _toloka, "Interstellar (2014) WEBRip", Magnet("0123456789abcdef0123456789abcdef01234567", MagnetPasskey), payloadKind: ReleasePayloadKind.Magnet);
        var echoedByFeed = await _releases.CreateFound(_member.User, _toloka, "Interstellar (2014) BDRemux");
        var echoedByRedirect = await _releases.CreateFound(_owner.User, _toloka, "Interstellar (2014) WEB-DL");
        var echoedByError = await _releases.CreateFound(_member.User, _toloka, "Interstellar (2014) DVDRip");
        var echoedMagnet = await _releases.CreateFound(
            _owner.User, _toloka, "Interstellar (2014) HDTV", Magnet("89abcdef0123456789abcdef0123456789abcdef", EchoedMagnetPasskey), payloadKind: ReleasePayloadKind.Magnet);

        //Act
        var sent = new List<(AuthorizedUser User, ReleaseEntity Release, HttpResponseMessage Response)>
        {
            // Taken: the feed's link fetched with the key put back; qBittorrent signed in and sent the .torrent.
            (_owner, torrent, await SendAsync(_owner, torrent)),
            // Taken: a magnet whose tracker carries the owner's passkey, sent to qBittorrent as is.
            (_member, magnet, await SendAsync(_member, magnet)),
        };

        // The feed's error quotes the link it was asked for (XML-escaped, as Prowlarr writes it), key and token
        // in its query, and both again in its own words.
        _feed.Intercept = request => VendorResponses.Text(
            $"""<error code="500" description="Fetching {SecurityElement.Escape(request.RequestUri!.AbsoluteUri)} failed: key {ApiKey}, link {LinkToken}" />""",
            HttpStatusCode.InternalServerError,
            "application/xml");
        sent.Add((_member, echoedByFeed, await SendAsync(_member, echoedByFeed)));
        _feed.Intercept = null;

        // Another .torrent for each of the next two, so what is tested is qBittorrent's answer to the add,
        // not that it already has the first one.
        _feed.TorrentFile = TorrentFile('b');
        // The add answered with a redirect whose path quotes the session it was sent with and the password.
        _qbittorrent.Intercept = request => IsAdd(request)
            ? VendorResponses.Redirect($"{_qbittorrent.BaseUrl}/{SessionOf(request)}/{QBittorrentPassword}/login")
            : null;
        sent.Add((_owner, echoedByRedirect, await SendAsync(_owner, echoedByRedirect)));

        _feed.TorrentFile = TorrentFile('c');
        // A 500 whose body repeats the session and the password.
        _qbittorrent.Intercept = request => IsAdd(request)
            ? VendorResponses.Text($"Session {SessionOf(request)} for admin:{QBittorrentPassword} failed", HttpStatusCode.InternalServerError)
            : null;
        sent.Add((_member, echoedByError, await SendAsync(_member, echoedByError)));

        // A magnet's add answered with a redirect to its own tracker's address, passkey and all.
        _qbittorrent.Intercept = request => IsAdd(request) ? VendorResponses.Redirect(EchoedTrackerAddress) : null;
        sent.Add((_owner, echoedMagnet, await SendAsync(_owner, echoedMagnet)));
        _qbittorrent.Intercept = null;

        // Every read of a download: the dialog's choices for each release, and each job, by whoever sent it.
        var reads = new List<HttpResponseMessage>();
        foreach (var (user, release, response) in sent)
        {
            reads.Add(await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={release.Id}", user.AccessToken));
            reads.Add(await _factory.GetHttpRequest(response.Headers.Location!.AbsolutePath, user.AccessToken));
        }

        //Assert
        // The sweep reads what it planted: each path ran as meant, with the planted values on the wire.
        var jobs = new List<DownloadJobDto>();
        foreach (var (_, _, response) in sent)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            jobs.Add((await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!);
        }

        // A member reads the app's sentence for a service's failure (DownloadsErrorVisibilityHttpTests); the
        // owner reads the service's own, masked.
        Assert.Equal(
            [
                (DownloadJobStatus.Submitted, (string?)null),
                (DownloadJobStatus.Submitted, null),
                (DownloadJobStatus.Failed, "“Toloka” couldn't hand over the release, so nothing was sent. Ask the owner to check “Toloka” under Integrations."),
                (DownloadJobStatus.Failed, "The address answered with a redirect to http://qbittorrent.test:8080/***/***/login. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else."),
                (DownloadJobStatus.Failed, "“qBittorrent” couldn't take the release, so nothing was sent. Ask the owner to check “qBittorrent” under Integrations."),
                (DownloadJobStatus.Failed, "The address answered with a redirect to ***. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else."),
            ],
            jobs.Select(job => (job.Status, job.Error)));
        // The rows keep the services' own sentences, masked, whoever sent the release.
        var memberRowErrors = await StoredErrorsAsync([jobs[2].Id, jobs[4].Id], cancellationToken);
        Assert.Equal(
            [
                "The Torznab feed failed to hand over the release (error 500: Fetching http://prowlarr.test:9696 failed: key ***, link ***).",
                "qBittorrent answered 500 Internal Server Error. Its own log says why.",
            ],
            memberRowErrors);
        Assert.All(reads, read => Assert.Equal(HttpStatusCode.OK, read.StatusCode));
        Assert.Contains(_factory.TorznabHttp.Requests, request => request.Uri.Query == $"?apikey={ApiKey}&link={LinkToken}");
        Assert.Contains(_factory.QBittorrentHttp.Requests, request => request.Body?.Contains($"password={QBittorrentPassword}", StringComparison.Ordinal) == true);
        Assert.Contains(_qbittorrent.AddRequests, add => add.Urls?.Contains(MagnetPasskey, StringComparison.Ordinal) == true);
        var sessions = _factory.QBittorrentHttp.Requests
            .SelectMany(request => request.HeaderValues("Cookie"))
            .Select(cookie => cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..])
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(sessions);

        string[] planted = [.. PlantedValues, .. sessions, .. await CiphertextsAsync(cancellationToken)];

        // No answer: every POST, every option list and every job read, to the owner and the member.
        foreach (var answer in sent.Select(send => send.Response).Concat(reads))
        {
            var text = ReadableText(await answer.Content.ReadAsStringAsync(cancellationToken));
            Assert.All(planted, secret => Assert.DoesNotContain(secret, text, StringComparison.Ordinal));
            // No download link of any kind reaches the browser (ADR 0004): not the gateway's, not a magnet.
            Assert.DoesNotContain("/download", text, StringComparison.Ordinal);
            Assert.DoesNotContain("magnet:", text, StringComparison.OrdinalIgnoreCase);
        }

        // No log line, message or exception — and no HttpClient logging, which would print every request URI.
        Assert.All(_factory.Logs.Entries, entry => Assert.All(planted, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
        Assert.Contains(_factory.Logs.Entries, entry => entry.Message.StartsWith("Download job ", StringComparison.Ordinal));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));

        // No job or audit row, in any column stored in clear.
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.DownloadJobs.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(sent.Count, rows.Count);
            Assert.All(rows, row =>
            {
                var clear = string.Join(
                    '\n',
                    row.Error,
                    row.ClientJobId,
                    row.DownloadClientName,
                    row.DestinationName,
                    row.ClientDestinationKey,
                    row.RequestedByUserName,
                    row.InfoHashV1,
                    row.InfoHashV2);
                Assert.All(planted, secret => Assert.DoesNotContain(secret, clear, StringComparison.Ordinal));
            });
            var audits = await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(sent.Count, audits.Count(audit => audit.Action == AuditAction.DownloadRequested));
            Assert.All(audits, audit =>
            {
                var clear = string.Join('\n', audit.ActorUserName, audit.TargetName);
                Assert.All(planted, secret => Assert.DoesNotContain(secret, clear, StringComparison.Ordinal));
            });
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The send of the release to qBittorrent's Movies as user, under a request id of its own.
    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _destination.Id));

    // Every endpoint of a download controller as the running app routes it.
    private List<RouteEndpoint> DownloadEndpoints() =>
        [.. _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
                && (controllerNamespace.Equals(DownloadsNamespace, StringComparison.Ordinal)
                    || controllerNamespace.StartsWith(DownloadsNamespace + ".", StringComparison.Ordinal)))];

    // Every sealed value the database holds: the integrations' secrets and the releases' download links.
    private async Task<List<string>> CiphertextsAsync(CancellationToken cancellationToken)
    {
        var ciphertexts = new List<string>();
        await _factory.WithDbAsync(async db =>
        {
            var integrations = await db.IntegrationConnections.AsNoTracking().ToListAsync(cancellationToken);
            ciphertexts.AddRange(integrations.SelectMany(row => new[] { row.ApiKeyEncrypted, row.PasswordEncrypted }).OfType<string>());
            ciphertexts.AddRange(await db.Releases.AsNoTracking().Select(release => release.PayloadLocatorEncrypted).ToListAsync(cancellationToken));
        });
        return ciphertexts;
    }

    // A magnet whose one tracker's announce address carries passkey in its path, escaped as a link carries it.
    private static string Magnet(string infoHash, string passkey) =>
        $"magnet:?xt=urn:btih:{infoHash}&dn=Interstellar&tr={Uri.EscapeDataString($"http://tracker.test/announce/{passkey}")}";

    // A one-byte single-file .torrent whose pieces are filler: each filler a different torrent.
    private static byte[] TorrentFile(char filler) =>
        Encoding.ASCII.GetBytes($"d8:announce19:http://tracker.test4:infod6:lengthi1e4:name5:y.mkv12:piece lengthi16384e6:pieces20:{new string(filler, 20)}ee");

    private static bool IsAdd(HttpRequestMessage request) =>
        request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/torrents/add";

    // The session id the request was sent with: the value of its QBT_SID_8080 cookie.
    private static string SessionOf(HttpRequestMessage request)
    {
        var cookie = request.Headers.NonValidated["Cookie"].Single();
        return cookie[(cookie.IndexOf('=', StringComparison.Ordinal) + 1)..];
    }

    /// <summary>The body and every JSON string in it decoded: the serializer writes <c>+</c> as
    /// <c>+</c>, so a base64 ciphertext is only found in its decoded form.</summary>
    private static string ReadableText(string body)
    {
        var text = new StringBuilder(body);
        using var document = JsonDocument.Parse(body);
        foreach (var value in Strings(document.RootElement))
            text.Append('\n').Append(value);
        return text.ToString();
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Strings(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        _ => [],
    };

    // The errors the rows of these jobs keep, in the order of the ids given.
    private async Task<string[]> StoredErrorsAsync(Guid[] jobIds, CancellationToken cancellationToken)
    {
        string[] errors = [];
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.DownloadJobs.AsNoTracking().Where(job => jobIds.Contains(job.Id)).ToListAsync(cancellationToken);
            errors = [.. jobIds.Select(id => rows.Single(row => row.Id == id).Error ?? "")];
        });
        return errors;
    }
}
