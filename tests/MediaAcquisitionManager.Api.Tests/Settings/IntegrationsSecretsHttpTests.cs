using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Settings;

// MAM-7 — the house redaction sweep (ADR 0007): a stored credential never leaves through the API and
// never reaches the log. Every settings read is found in the running app rather than listed by hand,
// so a read added later is swept the day it appears; secrets are planted in every type, real and fake.
public sealed class IntegrationsSecretsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/settings/integrations";
    // Spelled out rather than read from the convention: this pins where settings controllers live.
    private const string SettingsNamespace = "MediaAcquisitionManager.Api.Controllers.Settings";

    private const string JellyfinAddress = "http://jellyfin.test:8096";
    private const string TorznabAddress = "http://prowlarr.test:9696/1/api";
    private const string QBittorrentAddress = "http://qbittorrent.test:8080";
    private const string TransmissionAddress = FakeTransmissionServer.DefaultBaseUrl;

    // Distinctive, and 8 characters or more, so the masking never skips one for being short.
    private const string JellyfinApiKey = "planted-jellyfin-key-0001";
    private const string JellyfinNewApiKey = "planted-jellyfin-key-0002";
    private const string TorznabApiKey = "planted-torznab-key-0001";
    private const string DraftApiKey = "planted-draft-key-0001";
    private const string QBittorrentPassword = "planted-qbittorrent-password-1";
    private const string QBittorrentNewPassword = "planted-qbittorrent-password-2";
    private const string FakePassword = "planted-fake-password-0001";
    private const string FakeApiKey = "planted-fake-api-key-0001";
    private const string SessionId = "PlantedSession0001Zq3xY9LmN2pQ7r";
    private const string TransmissionPassword = "planted-transmission-password-1";
    private const string TransmissionNewPassword = "planted-transmission-password-2";
    private const string TransmissionDraftPassword = "planted-transmission-draft-0001";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public IntegrationsSecretsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_settings_read_of_today()
    {
        //Arrange

        //Act
        var reads = SettingsReads().Select(read => $"GET {read.RoutePattern.RawText}").ToHashSet();

        //Assert
        // Guards the guard: a discovery that misses some or all of these would let the sweep below pass
        // for less, or for nothing. A superset, so a settings read added later passes here and is swept
        // below.
        Assert.Superset(
            new HashSet<string>
            {
                "GET api/settings/destinations",
                "GET api/settings/destinations/{id:guid}",
                "GET api/settings/destinations/client-destinations/{downloadClientId:guid}",
                "GET api/settings/destinations/sections",
                "GET api/settings/integrations",
                "GET api/settings/integrations/adapter-types",
                "GET api/settings/integrations/{id:guid}",
                "GET api/settings/users",
                "GET api/settings/users/{id:guid}",
            },
            reads);
    }

    [Fact]
    public async Task Every_settings_read_answers_without_a_planted_secret_or_its_ciphertext()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var member = await new UserSeeder(_factory.Scope).Create("member");
        var seeded = new[]
        {
            await _integrations.Create("jellyfin", "Living room", JellyfinAddress, apiKey: JellyfinApiKey),
            await _integrations.Create("torznab", "Toloka", TorznabAddress, apiKey: TorznabApiKey),
            await _integrations.Create("qbittorrent", "Downloads", QBittorrentAddress, "admin", QBittorrentPassword),
            // MAM-30
            await _integrations.Create("transmission", "Transmission", TransmissionAddress, "admin", TransmissionPassword),
            await _integrations.Create(FakeIntegrationAdapter.TypeId, "Fake", userName: "admin", password: FakePassword, apiKey: FakeApiKey),
        };
        var ids = seeded.Select(integration => integration.Id).Concat([_owner.User.Id, member.Id]).ToList();
        var ciphertexts = new List<string>();
        await _factory.WithDbAsync(async db => ciphertexts.AddRange(
            (await db.IntegrationConnections.ToListAsync(cancellationToken))
                .SelectMany(row => new[] { row.PasswordEncrypted, row.ApiKeyEncrypted })
                .OfType<string>()));
        string[] planted = [JellyfinApiKey, TorznabApiKey, QBittorrentPassword, TransmissionPassword, FakePassword, FakeApiKey, .. ciphertexts];

        //Act
        // Each read with each seeded id in every id slot: most combinations are 404s, and the ones that
        // match read a row carrying secrets.
        var answers = new List<(string Url, HttpStatusCode Status, string Body)>();
        foreach (var url in SettingsReads().SelectMany(read => UrlsFor(read.RoutePattern, ids)))
        {
            var response = await _factory.GetHttpRequest(url, _owner.AccessToken);
            answers.Add((url, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        //Assert
        Assert.NotEmpty(answers);
        // One key each for Jellyfin and Torznab, qBittorrent's and Transmission's passwords, and both of the fake's.
        Assert.Equal(6, ciphertexts.Count);
        // The sweep really read every planted row, one by one and in the list.
        Assert.All(seeded, integration => Assert.Contains(
            answers, answer => answer.Url == $"/api/settings/integrations/{integration.Id}" && answer.Status == HttpStatusCode.OK));
        Assert.Contains(answers, answer => answer.Url == "/api/settings/integrations" && answer.Status == HttpStatusCode.OK
            && seeded.All(integration => answer.Body.Contains(integration.Id.ToString(), StringComparison.Ordinal)));
        Assert.All(answers, answer =>
        {
            var text = ReadableText(answer.Body);
            Assert.All(planted, secret => Assert.DoesNotContain(secret, text, StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Integration_answers_carry_has_flags_and_no_secret_properties()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var created = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new CreateIntegrationRequest(
            FakeIntegrationAdapter.TypeId, "Fake", "http://127.0.0.1:9", "admin", Set(FakePassword), Set(FakeApiKey)));
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        var id = (await created.Content.ReadFromJsonAsync<IntegrationDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Id;
        var listBody = await (await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken)).Content.ReadAsStringAsync(cancellationToken);
        var getBody = await (await _factory.GetHttpRequest($"{BaseUrl}/{id}", _owner.AccessToken)).Content.ReadAsStringAsync(cancellationToken);

        //Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        foreach (var body in new[] { createdBody, listBody, getBody })
        {
            var names = PropertyNames(JsonDocument.Parse(body).RootElement).ToList();
            Assert.Contains("hasPassword", names);
            Assert.Contains("hasApiKey", names);
            Assert.DoesNotContain(names, name =>
                name.Equals("password", StringComparison.OrdinalIgnoreCase)
                || name.Equals("apiKey", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Encrypted", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(FakePassword, body, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeApiKey, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Nothing_logged_while_integrations_are_used_carries_a_secret()
    {
        //Arrange
        var answers = new List<HttpResponseMessage>();

        //Act
        // Created and changed through the API, so every secret passes through the service as typed.
        var jellyfin = await CreateAsync(new CreateIntegrationRequest("jellyfin", "Living room", JellyfinAddress, ApiKey: Set(JellyfinApiKey)), answers);
        var torznab = await CreateAsync(new CreateIntegrationRequest("torznab", "Toloka", TorznabAddress, ApiKey: Set(TorznabApiKey)), answers);
        var qbittorrent = await CreateAsync(
            new CreateIntegrationRequest("qbittorrent", "Downloads", QBittorrentAddress, "admin", Set(QBittorrentPassword)), answers);
        answers.Add(await _factory.PutHttpRequest(
            $"{BaseUrl}/{jellyfin}", _owner.AccessToken, new UpdateIntegrationRequest("Living room", JellyfinAddress, ApiKey: Set(JellyfinNewApiKey))));
        answers.Add(await _factory.PutHttpRequest(
            $"{BaseUrl}/{qbittorrent}", _owner.AccessToken, new UpdateIntegrationRequest("Downloads", QBittorrentAddress, "admin", Set(QBittorrentNewPassword))));

        // Saved tests, each outcome in turn: success, a refused credential, a redirect whose target
        // carries the key, and an error body that repeats it.
        _factory.JellyfinHttp.Handler = (_, _) => VendorResponses.Sample("Jellyfin/system-info.json", "application/json");
        answers.Add(await TestAsync(jellyfin));
        _factory.JellyfinHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        answers.Add(await TestAsync(jellyfin));
        _factory.JellyfinHttp.Handler = (_, _) => VendorResponses.Redirect($"http://sso.test/sign-in?api_key={JellyfinNewApiKey}");
        answers.Add(await TestAsync(jellyfin));
        _factory.TorznabHttp.Handler = (_, _) => TorznabError(TorznabApiKey);
        answers.Add(await TestAsync(torznab));
        _factory.QBittorrentHttp.Handler = (request, _) => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; HttpOnly; path=/")
            : VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain");
        answers.Add(await TestAsync(qbittorrent));
        _factory.QBittorrentHttp.Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        answers.Add(await TestAsync(qbittorrent));

        // Draft tests: a new one with a typed key the service repeats, and an edit keeping the stored password.
        _factory.TorznabHttp.Handler = (_, _) => TorznabError(DraftApiKey);
        answers.Add(await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(null, "torznab", TorznabAddress, ApiKey: Set(DraftApiKey))));
        _factory.QBittorrentHttp.Handler = (request, _) => request.RequestUri!.AbsolutePath == "/api/v2/auth/login"
            ? VendorResponses.WithSetCookie(new HttpResponseMessage(HttpStatusCode.NoContent), $"QBT_SID_8080={SessionId}; HttpOnly; path=/")
            : VendorResponses.Sample("QBittorrent/app-version.txt", "text/plain");
        answers.Add(await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(qbittorrent, null, QBittorrentAddress, "admin")));

        //Assert
        // Guards the guard: every step ran, and the log has the lines a sweep should look through.
        Assert.All(answers, answer => Assert.True(answer.IsSuccessStatusCode, $"{answer.RequestMessage?.RequestUri} answered {answer.StatusCode}."));
        Assert.Equal(8, _factory.Logs.Entries.Count(entry => entry.Message.StartsWith("Tested integration", StringComparison.Ordinal)));
        string[] planted = [JellyfinApiKey, JellyfinNewApiKey, TorznabApiKey, DraftApiKey, QBittorrentPassword, QBittorrentNewPassword, SessionId];
        Assert.All(_factory.Logs.Entries, entry => Assert.All(planted, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
        // The client factory's logging handlers write each request URI, and Torznab's key travels in
        // the query (ADR 0004): pins that the integration clients have none (RemoveAllLoggers).
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nothing_answered_or_logged_while_a_transmission_is_used_carries_its_password_basic_credentials_or_session_id()
    {
        //Arrange
        // MAM-30 — every Transmission request carries the Basic credentials and a session id, and Transmission's own
        // refusals are quoted: a service that echoes them must not get them into a sentence or the log.
        var cancellationToken = TestContext.Current.CancellationToken;
        var transmission = new FakeTransmissionServer { Password = TransmissionNewPassword };
        transmission.Arm(_factory.TransmissionHttp);
        var answers = new List<HttpResponseMessage>();

        //Act
        var id = await CreateAsync(new CreateIntegrationRequest("transmission", "Transmission", TransmissionAddress, "admin", Set(TransmissionPassword)), answers);
        // Refused: the stored password isn't Transmission's.
        answers.Add(await TestAsync(id));
        answers.Add(await _factory.PutHttpRequest(
            $"{BaseUrl}/{id}", _owner.AccessToken, new UpdateIntegrationRequest("Transmission", TransmissionAddress, "admin", Set(TransmissionNewPassword))));
        answers.Add(await TestAsync(id));
        // A refusal that repeats the session id, the password and the Basic credentials.
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"admin:{TransmissionNewPassword}"));
        transmission.RpcIntercept = _ => new JsonObject
        {
            ["arguments"] = new JsonObject(),
            ["result"] = $"session {transmission.SessionId} for admin:{TransmissionNewPassword} ({basic})",
        };
        answers.Add(await TestAsync(id));
        // A draft with a typed password Transmission refuses.
        transmission.RpcIntercept = null;
        answers.Add(await _factory.PostHttpRequest(
            $"{BaseUrl}/test", _owner.AccessToken, new TestIntegrationRequest(null, "transmission", TransmissionAddress, "admin", Set(TransmissionDraftPassword))));
        var bodies = new List<string>();
        foreach (var answer in answers)
            bodies.Add(await answer.Content.ReadAsStringAsync(cancellationToken));

        //Assert
        Assert.All(answers, answer => Assert.True(answer.IsSuccessStatusCode, $"{answer.RequestMessage?.RequestUri} answered {answer.StatusCode}."));
        Assert.Contains(bodies, body => body.Contains("Connected to Transmission 4.1.3.", StringComparison.Ordinal));
        Assert.Contains(bodies, body => body.Contains("Transmission refused the request: session *** for admin:*** (***).", StringComparison.Ordinal));
        string[] planted = [TransmissionPassword, TransmissionNewPassword, TransmissionDraftPassword, basic, transmission.SessionId];
        Assert.All(bodies, body => Assert.All(planted, secret => Assert.DoesNotContain(secret, body, StringComparison.Ordinal)));
        Assert.All(_factory.Logs.Entries, entry => Assert.All(planted, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
        await _factory.WithDbAsync(async db => Assert.All(
            await db.AuditEvents.ToListAsync(cancellationToken),
            audit => Assert.All(planted, secret => Assert.DoesNotContain(secret, $"{audit.ActorUserName} {audit.TargetName}", StringComparison.Ordinal))));
    }

    private static SecretValueUpsert Set(string value) => new(SecretUpsertMode.Set, value);

    private static HttpResponseMessage TorznabError(string echoedKey) => VendorResponses.Text(
        $"""<?xml version="1.0" encoding="UTF-8"?><error code="100" description="Invalid API key {echoedKey}" />""",
        HttpStatusCode.OK,
        "application/xml");

    private async Task<Guid> CreateAsync(CreateIntegrationRequest request, List<HttpResponseMessage> answers)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, request);
        answers.Add(response);
        var created = await response.Content.ReadFromJsonAsync<IntegrationDto>(
            MediaAcquisitionManagerApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        return created!.Id;
    }

    private Task<HttpResponseMessage> TestAsync(Guid integrationId) =>
        _factory.PostHttpRequest($"{BaseUrl}/{integrationId}/test", _owner.AccessToken);

    // Every GET of a settings controller as the running app routes it.
    private List<RouteEndpoint> SettingsReads() =>
        [.. _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => IsSettingsEndpoint(endpoint)
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Any(HttpMethods.IsGet) == true)];

    private static bool IsSettingsEndpoint(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(SettingsNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(SettingsNamespace + ".", StringComparison.Ordinal));

    // The route with its parameters filled in: once per id when it takes a guid, once otherwise.
    private static IEnumerable<string> UrlsFor(RoutePattern pattern, IReadOnlyList<Guid> ids)
    {
        var takesId = pattern.Parameters.Any(parameter => HasConstraint(parameter, "guid"));
        return takesId ? ids.Select(id => UrlFor(pattern, id)) : [UrlFor(pattern, Guid.Empty)];
    }

    private static string UrlFor(RoutePattern pattern, Guid id) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => TextOf(part, id)))));

    private static string TextOf(RoutePatternPart part, Guid id) => part switch
    {
        RoutePatternLiteralPart literal => literal.Content,
        RoutePatternSeparatorPart separator => separator.Content,
        RoutePatternParameterPart parameter when HasConstraint(parameter, "guid") => id.ToString(),
        RoutePatternParameterPart parameter when HasConstraint(parameter, "int") || HasConstraint(parameter, "long") => "1",
        RoutePatternParameterPart => "sample",
        _ => throw new InvalidOperationException($"Unexpected route part {part}."),
    };

    private static bool HasConstraint(RoutePatternParameterPart parameter, string name) =>
        parameter.ParameterPolicies.Any(policy => string.Equals(policy.Content, name, StringComparison.OrdinalIgnoreCase));

    // The body as sent plus every string in it as JSON decodes it: the serializer escapes '+' in a
    // base64 ciphertext as +, so a leaked ciphertext wouldn't match the raw text alone.
    private static string ReadableText(string body)
    {
        var text = new StringBuilder(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            foreach (var value in Strings(document.RootElement))
                text.Append('\n').Append(value);
        }
        catch (JsonException)
        {
            // Not JSON (an empty 404): the raw text is all there is.
        }

        return text.ToString();
    }

    private static IEnumerable<string> Strings(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Strings(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Strings),
        _ => [],
    };

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => PropertyNames(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
