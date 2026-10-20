using System.Net;
using System.Text;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12, ADR 0004 and ADR 0007 — the release search's sweep (ReleaseSearchSecretsHttpTests) for the
// stored search: every credential a release source's answer can carry — the gateway's API key, its link=
// token, the owner's forum session, a tracker passkey (in a guid, a magnet, a page link, an error
// description, an id attribute) — is planted where a real feed puts it, and none comes back from any of
// the search's three endpoints to the owner or a member, is stored with the search or its sources, or
// reaches the log. Only the sealed download link holds what it needs, and it never leaves.
public sealed class SearchesSecretsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    // Spelled out rather than read from the controller: this pins where the search's controllers live.
    private const string SearchNamespace = "MediaAcquisitionManager.Api.Controllers.Search";

    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string FailingAddress = "http://prowlarr.test:9696/2/api";

    // Distinctive, URL-safe and 8 characters or more, so masking never skips one and the raw and escaped
    // forms are the same text.
    private const string ApiKey = "planted-torznab-key-0001";
    private const string LinkToken = "planted-link-token-0001";
    private const string SessionId = "planted-sid-0001";
    private const string PathPasskey = "planted-passkey-0001";
    private const string QueryPasskey = "planted-passkey-0002";
    private const string MagnetPasskey = "planted-passkey-0003";
    private const string PagePasskey = "planted-passkey-0004";
    private const string ErrorPasskey = "planted-passkey-0005";
    private const string AttributePasskey = "planted-passkey-0006";

    private static readonly string[] PlantedValues =
        [ApiKey, LinkToken, SessionId, PathPasskey, QueryPasskey, MagnetPasskey, PagePasskey, ErrorPasskey, AttributePasskey];

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public SearchesSecretsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        await integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await integrations.Create("torznab", "Failing", FailingAddress, apiKey: ApiKey);

        // Toloka, whose releases carry every credential a feed can list, and a second feed that refuses
        // the key with a description quoting a tracker URL, the key and the session.
        var toloka = new FakeTorznabServer(TolokaAddress, ApiKey) { SearchXml = TorznabSamples.Feed(PlantedItems()) };
        var failing = new FakeTorznabServer(FailingAddress, ApiKey)
        {
            Intercept = request => request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal)
                ? VendorResponses.Text(
                    $"""<error code="100" description="Invalid API key {ApiKey} for https://tracker.test/rss/{ErrorPasskey}/x.torrent?passkey={ErrorPasskey} sid={SessionId}" />""",
                    HttpStatusCode.Unauthorized,
                    "application/xml")
                : null,
        };
        FakeTorznabServer.Arm(_factory.TorznabHttp, toloka, failing);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Discovery_finds_every_search_endpoint_of_today()
    {
        //Arrange

        //Act
        var endpoints = SearchEndpoints()
            .Select(endpoint => $"{endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Single()} {endpoint.RoutePattern.RawText}")
            .ToHashSet(StringComparer.Ordinal);

        //Assert
        // Guards the sweep: the namespace's endpoints are the three swept below. One added later needs a
        // sweep of its own.
        Assert.Equal(["POST api/searches", "GET api/searches/{id:guid}", "GET api/searches/{id:guid}/releases"], endpoints);
    }

    [Fact]
    public async Task Searches_answer_no_planted_credential_or_ciphertext_to_the_owner_or_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] planted = [.. PlantedValues, .. await IntegrationCiphertextsAsync(cancellationToken)];

        //Act
        var bodies = new List<string>();
        foreach (var user in new[] { _owner, _member })
        {
            var created = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SearchRequest("Interstellar"));
            var path = created.Headers.Location!.AbsolutePath;
            bodies.Add(await created.Content.ReadAsStringAsync(cancellationToken));
            bodies.Add(await (await _factory.GetHttpRequest(path, user.AccessToken)).Content.ReadAsStringAsync(cancellationToken));
            bodies.Add(await (await _factory.GetHttpRequest($"{path}/releases?pageSize=100", user.AccessToken)).Content.ReadAsStringAsync(cancellationToken));
        }

        //Assert
        string[] releaseCiphertexts = [.. await ReleaseCiphertextsAsync(cancellationToken)];
        // The sweep reads what it planted: each list holds every planted release, and the failing source
        // told the owner what its own error description said.
        Assert.Equal(2 * PlantedItems().Length, releaseCiphertexts.Length);
        Assert.All([bodies[2], bodies[5]], body => Assert.Equal(
            PlantedItems().Length,
            JsonSerializer.Deserialize<SearchReleaseListResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!.TotalCount));
        Assert.Contains("refused the API key (error 100:", PseudoText.Strip(bodies[0]), StringComparison.Ordinal);
        Assert.Contains("refused the API key (error 100:", PseudoText.Strip(bodies[1]), StringComparison.Ordinal);
        Assert.All(bodies, body =>
        {
            var text = ReadableText(body);
            Assert.All([.. planted, .. releaseCiphertexts], secret => Assert.DoesNotContain(secret, text, StringComparison.Ordinal));
            // A session parameter only ever with its value masked.
            Assert.DoesNotMatch(@"(?i)sid=(?!\*\*\*)", text);
            // No download link of any kind reaches the browser (ADR 0004): not the gateway's, not a magnet.
            Assert.DoesNotContain("/download", text, StringComparison.Ordinal);
            Assert.DoesNotContain("magnet:", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Search_stores_no_planted_credential_outside_the_sealed_download_link()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var encryption = _factory.Resolve<IEncryptionService>();
        await _factory.WithDbAsync(async db =>
        {
            // The failing source's sentence is kept for the owner, masked as the adapter and the second pass
            // left it (ADR 0007).
            var failing = await db.SearchSources.AsNoTracking().SingleAsync(source => source.Name == "Failing", cancellationToken);
            Assert.Contains("refused the API key (error 100:", TestTexts.En(failing.Error), StringComparison.Ordinal);
            var search = await db.Searches.AsNoTracking().SingleAsync(cancellationToken);
            var stored = string.Join('\n', [
                search.Text, search.ImdbId, search.TmdbId,
                .. (await db.SearchSources.AsNoTracking().ToListAsync(cancellationToken)).SelectMany(source => new[] { source.Name, TestTexts.En(source.Error) })]);
            Assert.All(PlantedValues, secret => Assert.DoesNotContain(secret, stored, StringComparison.Ordinal));
            Assert.DoesNotMatch(@"(?i)sid=(?!\*\*\*)", stored);

            var releases = await db.Releases.AsNoTracking().ToListAsync(cancellationToken);
            Assert.Equal(PlantedItems().Length, releases.Count);
            Assert.All(releases, release =>
            {
                // Every column stored in clear, as the row holds it.
                var clear = string.Join(
                    '\n', release.Title, release.SourceItemId, release.SourcePageUrl, release.SourceName, release.InfoHash, release.ImdbId, release.TmdbId);
                Assert.All(PlantedValues, secret => Assert.DoesNotContain(secret, clear, StringComparison.Ordinal));
                Assert.DoesNotContain("sid=", clear, StringComparison.OrdinalIgnoreCase);
                // The sealed link holds the link token it needs, never the key.
                Assert.True(encryption.TryDecrypt(release.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(release.Id), out var link));
                Assert.DoesNotContain(ApiKey, link, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    public async Task Nothing_logged_while_searching_and_reading_a_search_carries_a_planted_credential()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _factory.Logs.Clear();

        //Act
        var created = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));
        var path = created.Headers.Location!.AbsolutePath;
        await _factory.GetHttpRequest(path, _owner.AccessToken);
        await _factory.GetHttpRequest($"{path}/releases?sort=largest", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        // The search logged its sources' outcomes: the check below read real entries.
        Assert.Contains(_factory.Logs.Entries, entry => entry.Message.StartsWith("Release search of integration", StringComparison.Ordinal));
        string[] planted = [.. PlantedValues, .. await IntegrationCiphertextsAsync(cancellationToken)];
        Assert.All(_factory.Logs.Entries, entry => Assert.All(planted, secret =>
        {
            Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "", StringComparison.Ordinal);
        }));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Category.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One release per place a real feed puts a credential.</summary>
    private static string[] PlantedItems()
    {
        var download = $"http://prowlarr.test:9696/1/download?apikey={ApiKey}&link={LinkToken}&file=Interstellar";
        const string movie = "<category>2000</category>";
        return
        [
            // Toloka's own shape: the topic link with the owner's session, in guid and comments.
            TorznabSamples.Item("Interstellar (2014) BDRemux", download, $"https://toloka.to/t1?sid={SessionId}", $"https://toloka.to/t1?SID={SessionId}", movie),
            TorznabSamples.Item("Interstellar (2014) BDRip", download, $"https://toloka.to/t2?a=1;sid={SessionId}", $"https://toloka.to/t2?sid={SessionId}", movie),
            // Prowlarr's guid for an indexer without one: the tracker's download link, passkeys and all.
            TorznabSamples.Item(
                "Interstellar (2014) WEB-DL", download, $"https://tracker.test/rss/download/1/{PathPasskey}/x.torrent?passkey={QueryPasskey}", extra: movie),
            // A magnet guid and a magnet download, their trackers carrying the passkey.
            TorznabSamples.Item(
                "Interstellar (2014) WEBRip",
                $"magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&tr=https%3A%2F%2Ftracker.test%2F{MagnetPasskey}%2Fannounce",
                $"magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&tr=https%3A%2F%2Ftracker.test%2F{MagnetPasskey}%2Fannounce",
                extra: movie),
            // A page link that is a download, and one that names the passkey or repeats the key.
            TorznabSamples.Item("Interstellar (2014) DVDRip", download, comments: $"https://tracker.test/dl/{PagePasskey}/x", extra: movie),
            TorznabSamples.Item("Interstellar (2014) HDTV", download, comments: $"https://toloka.to/t3?passkey={PagePasskey}", extra: movie),
            TorznabSamples.Item("Interstellar (2014) TVRip", download, comments: $"https://toloka.to/t4?ref={ApiKey}", extra: movie),
            // A title that echoes the key.
            TorznabSamples.Item($"Interstellar (2014) {ApiKey}", download, extra: movie),
            // MAM-12 — id attributes holding no id: they are read, and must not carry anything through.
            TorznabSamples.Item(
                "Interstellar (2014) CAMRip",
                download,
                extra: movie + $"""<torznab:attr name="imdbid" value="{AttributePasskey}" /><torznab:attr name="tmdbid" value="{ApiKey}" />"""),
        ];
    }

    private async Task<List<string>> IntegrationCiphertextsAsync(CancellationToken cancellationToken)
    {
        var ciphertexts = new List<string>();
        await _factory.WithDbAsync(async db => ciphertexts.AddRange(
            (await db.IntegrationConnections.AsNoTracking().ToListAsync(cancellationToken)).Select(row => row.ApiKeyEncrypted).OfType<string>()));
        return ciphertexts;
    }

    private async Task<List<string>> ReleaseCiphertextsAsync(CancellationToken cancellationToken)
    {
        var ciphertexts = new List<string>();
        await _factory.WithDbAsync(async db => ciphertexts.AddRange(
            await db.Releases.AsNoTracking().Select(release => release.PayloadLocatorEncrypted).ToListAsync(cancellationToken)));
        return ciphertexts;
    }

    // Every endpoint of a search controller as the running app routes it.
    private List<RouteEndpoint> SearchEndpoints() =>
        [.. _factory.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace is { } controllerNamespace
                && (controllerNamespace.Equals(SearchNamespace, StringComparison.Ordinal)
                    || controllerNamespace.StartsWith(SearchNamespace + ".", StringComparison.Ordinal)))];

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
}
