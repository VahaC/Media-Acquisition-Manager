using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Downloads;

// MAM-30, ADR 0007 — what Transmission is sent with never comes back: its password, the Basic credentials every request
// carries and the session ids it hands out (rotated once on the way) are planted where Transmission's answers would echo
// them — a folder it can't see, an add it refuses, a torrent-get the poller sends, the connection test — while the owner
// and a member list destinations, send, follow, read their jobs and test the connection through the real adapter over
// HTTP. Each echo reaches its sentence masked; none of the planted values, nor a stored ciphertext, is in an answer, a log
// line or a job or audit row. The integrations pages' own sweep is IntegrationsSecretsHttpTests'.
public sealed class DownloadsTransmissionSecretsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/downloads";
    private const string DestinationsUrl = "/api/settings/destinations";
    private const string IntegrationsUrl = "/api/settings/integrations";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string ApiKey = "planted-torznab-key-0030";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";
    private const string MoviesFolder = "/downloads/movies";
    private const string ShowsFolder = "/downloads/tv-shows";

    // Distinctive and 8 characters or more, so masking never skips it.
    private const string TransmissionPassword = "planted-transmission-password-0030";

    // What each echo reads as once masked, spelled out rather than read from product code.
    private const string Echoed = "session *** for admin:*** (***)";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _feed = null!;
    private FakeTransmissionServer _transmission = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private IntegrationConnectionEntity _client = null!;
    private DestinationMappingEntity _movies = null!;
    private string _basic = null!;

    public DownloadsTransmissionSecretsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _feed = new FakeTorznabServer(TolokaAddress, ApiKey) { TorrentFile = TestTorrent.V1("Film.2001.mkv", TestTorrent.Bytes(20, seed: 1)) };
        _feed.Arm(_factory.TorznabHttp);
        _toloka = await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        var libraries = new LibraryLocationSeeder(_factory.Scope);
        await libraries.Create(jellyfin, "Фільми", "/media/movies");
        await libraries.Create(jellyfin, "Серіали", "/media/tv-shows");
        _transmission = new FakeTransmissionServer { Password = TransmissionPassword };
        _transmission.AddFolder(MoviesFolder);
        _transmission.Arm(_factory.TransmissionHttp);
        _client = await integrations.Create(
            "transmission",
            "Transmission",
            _transmission.BaseUrl,
            userName: FakeTransmissionServer.DefaultUserName,
            password: TransmissionPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        _movies = await new DestinationMappingSeeder(_factory.Scope).Create(_client, "Фільми", MoviesFolder, isDefault: true);
        _basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{FakeTransmissionServer.DefaultUserName}:{TransmissionPassword}"));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Nothing_answered_logged_or_stored_while_transmission_is_used_carries_its_password_basic_credentials_or_session_ids()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var forOwner = await _releases.CreateFound(_owner.User, _toloka, "Film (2001) BDRip 1080p");
        var forMember = await _releases.CreateFound(_member.User, _toloka, "Film (2001) BDRip 1080p");
        var answers = new List<HttpResponseMessage>();

        //Act
        // The Add destination dialog: Transmission can't see the series' folder, and says why in its own words.
        _transmission.RpcIntercept = call => call.Method == "free-space" && call.Arguments["path"]?.GetValue<string>() == ShowsFolder ? Echo() : null;
        var listed = await _factory.GetHttpRequest($"{DestinationsUrl}/client-destinations/{_client.Id}", _owner.AccessToken);
        answers.Add(listed);
        // The owner's send goes; the member's add is refused with an echo.
        var ownersSend = await SendAsync(_owner, forOwner);
        answers.Add(ownersSend);
        _transmission.RotateSession();
        _transmission.RpcIntercept = call => call.Method == "torrent-add" ? Echo() : null;
        _feed.TorrentFile = TestTorrent.V1("Film.2001.Remastered.mkv", TestTorrent.Bytes(20, seed: 2));
        var membersSend = await SendAsync(_member, forMember);
        answers.Add(membersSend);
        // A pass of the poller whose torrent-get is refused with an echo, then every read of the jobs.
        _transmission.RpcIntercept = call => call.Method == "torrent-get" ? Echo() : null;
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await _factory.PollDownloadJobsAsync(cancellationToken);
        var ownersJob = await ReadJobAsync(ownersSend, cancellationToken);
        var membersJob = await ReadJobAsync(membersSend, cancellationToken);
        answers.Add(await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken));
        answers.Add(await _factory.GetHttpRequest(BaseUrl, _member.AccessToken));
        var ownersRead = await _factory.GetHttpRequest($"{BaseUrl}/{ownersJob.Id}", _owner.AccessToken);
        answers.Add(ownersRead);
        answers.Add(await _factory.GetHttpRequest($"{BaseUrl}/{membersJob.Id}", _member.AccessToken));
        answers.Add(await _factory.GetHttpRequest($"{BaseUrl}/{ownersJob.Id}/files", _owner.AccessToken));
        answers.Add(await _factory.GetHttpRequest($"{BaseUrl}/options?releaseId={forOwner.Id}", _owner.AccessToken));
        answers.Add(await _factory.GetHttpRequest($"{BaseUrl}/overwrite-check?destinationId={_movies.Id}", _member.AccessToken));
        // The connection test, its session-get refused with an echo.
        _transmission.RpcIntercept = call => call.Method == "session-get" ? Echo() : null;
        var test = await _factory.PostHttpRequest($"{IntegrationsUrl}/{_client.Id}/test", _owner.AccessToken);
        answers.Add(test);

        //Assert
        // The sweep reads what it planted: each echo reached its sentence, masked.
        var destinations = (await listed.Content.ReadFromJsonAsync<ClientDestinationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal(
            $"Transmission can't see “{ShowsFolder}” ({Echoed}). Mount that folder into Transmission, or add a path mapping on this integration that leads there, then look again.",
            Assert.Single(destinations.Items, item => item.Key == ShowsFolder).Check.Problem);
        Assert.Equal((DownloadJobStatus.Submitted, (string?)null), (ownersJob.Status, ownersJob.Error));
        Assert.Equal(
            $"Transmission refused the request: {Echoed}.",
            (await ownersRead.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.BlockReason);
        Assert.Equal(
            (DownloadJobStatus.Failed, (string?)"“Transmission” couldn't take the release, so nothing was sent. Ask the owner to check “Transmission” under Integrations."),
            (membersJob.Status, membersJob.Error));
        Assert.Equal(
            new ConnectionTestResponse(IntegrationCallStatus.Refused, $"Transmission refused the request: {Echoed}."),
            await test.Content.ReadFromJsonAsync<ConnectionTestResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken));
        var sessions = _factory.TransmissionHttp.Requests
            .SelectMany(request => request.HeaderValues(FakeTransmissionServer.SessionIdHeader))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(sessions.Count >= 2, "The rotated session id was sent too.");
        Assert.All(
            _factory.TransmissionHttp.Requests,
            request => Assert.Equal([$"Basic {_basic}"], request.HeaderValues("Authorization")));

        string[] planted = [TransmissionPassword, _basic, .. sessions, .. await CiphertextsAsync(cancellationToken)];

        // No answer, to the owner or the member.
        foreach (var answer in answers)
        {
            Assert.True(answer.IsSuccessStatusCode, $"{answer.RequestMessage?.RequestUri} answered {(int)answer.StatusCode}.");
            var text = ReadableText(await answer.Content.ReadAsStringAsync(cancellationToken));
            Assert.All(planted, secret => Assert.DoesNotContain(secret, text, StringComparison.Ordinal));
        }

        // No log line, message or exception — and no HttpClient logging, which would print the requests.
        Assert.All(_factory.Logs.Entries, entry => Assert.All(planted, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));

        // No job or audit row, in any column stored in clear: the rows keep Transmission's sentences, masked.
        await _factory.WithDbAsync(async db =>
        {
            var rows = await db.DownloadJobs.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => TestTexts.En(row.Error) == $"Transmission refused the torrent: {Echoed}.");
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
                    row.InfoHashV2,
                    row.BlockReason);
                Assert.All(planted, secret => Assert.DoesNotContain(secret, clear, StringComparison.Ordinal));
            });
            var audits = await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
            Assert.NotEmpty(audits);
            Assert.All(audits, audit =>
            {
                var clear = string.Join('\n', audit.ActorUserName, audit.TargetName);
                Assert.All(planted, secret => Assert.DoesNotContain(secret, clear, StringComparison.Ordinal));
            });
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SendAsync(AuthorizedUser user, ReleaseEntity release) =>
        _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SendDownloadRequest(Guid.NewGuid(), release.Id, _client.Id, _movies.Id));

    /// <summary>A refusal whose <c>result</c> quotes the session id Transmission holds now, the user name and password, and
    /// the Basic credentials they make.</summary>
    private JsonObject Echo() => new()
    {
        ["arguments"] = new JsonObject(),
        ["result"] = $"session {_transmission.SessionId} for {FakeTransmissionServer.DefaultUserName}:{TransmissionPassword} ({_basic})",
    };

    private static async Task<DownloadJobDto> ReadJobAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<DownloadJobDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

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

    /// <summary>The body and every JSON string in it decoded: the serializer escapes some characters, so a value is only
    /// found in its decoded form.</summary>
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
}
