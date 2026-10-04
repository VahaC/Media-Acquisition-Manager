using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Releases;

// MAM-10 — every enabled release source answers for itself (ADR 0004: "a Toloka failure shows an
// explicit source-specific error" while the other sources keep answering): two feeds on one gateway,
// one of them failing in each way a source can, and the sources the app can't or doesn't ask.
public sealed class ReleaseSearchSourcesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/releases/search";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _other = null!;

    public ReleaseSearchSourcesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _toloka = new FakeTorznabServer(TolokaAddress, ApiKey);
        _other = new FakeTorznabServer(OtherAddress, ApiKey);
        FakeTorznabServer.Arm(_factory.TorznabHttp, _toloka, _other);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Search_with_one_failing_source_is_200_with_the_other_sources_releases()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        // Prowlarr's answer for an indexer it paused after failures (ADR 0004).
        _toloka.Intercept = request => request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal)
            ? VendorResponses.Text(
                """<error code="429" description="Indexer is disabled till 01/10/2026 14:05:00 due to recent failures." />""",
                HttpStatusCode.TooManyRequests,
                "application/xml")
            : null;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        // Ordered by name.
        Assert.Equal(["Other", "Toloka"], sources.Select(source => source.Name));
        Assert.Equal(IntegrationCallStatus.Succeeded, sources[0].Status);
        Assert.Equal(SampleMovieCount, sources[0].Releases.Count);
        Assert.Equal(IntegrationCallStatus.Refused, sources[1].Status);
        Assert.Null(sources[1].NotSearchedReason);
        Assert.Equal(
            "The Torznab feed is refusing requests for now (error 429: Indexer is disabled till 01/10/2026 14:05:00 due to recent failures.). Try again in a few minutes.",
            sources[1].Error);
        Assert.Empty(sources[1].Releases);
        await _factory.WithDbAsync(async db => Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Search_of_a_source_that_refuses_its_key_says_so_with_prowlarrs_real_answer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        // Prowlarr's recorded answer to a wrong key, to every request: a bare 401 with no body.
        _toloka.Intercept = _ => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        var toloka = Assert.Single(sources, source => source.Name == "Toloka");
        Assert.Equal(IntegrationCallStatus.AuthenticationFailed, toloka.Status);
        Assert.Equal("The Torznab feed refused the API key (401 Unauthorized).", toloka.Error);
        Assert.Empty(toloka.Releases);
        Assert.Equal(SampleMovieCount, Assert.Single(sources, source => source.Name == "Other").Releases.Count);
    }

    [Fact]
    public async Task Search_as_a_member_gets_a_failed_sources_status_without_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        var toloka = Assert.Single(sources, source => source.Name == "Toloka");
        Assert.Equal(IntegrationCallStatus.Unreachable, toloka.Status);
        // The sentence names the gateway's address, which is the owner's (the MAM-8 rule).
        Assert.Null(toloka.Error);
        Assert.Equal(SampleMovieCount, Assert.Single(sources, source => source.Name == "Other").Releases.Count);
    }

    [Fact]
    public async Task Search_as_the_owner_gets_a_failed_sources_sentence_with_its_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var toloka = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(
            $"Couldn't reach Torznab at {TolokaAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.",
            toloka.Error);
    }

    [Fact]
    public async Task Search_leaves_a_switched_off_source_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey, isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal("Toloka", Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Name);
        Assert.DoesNotContain(_factory.TorznabHttp.Requests, request => request.Uri.AbsolutePath.StartsWith("/2/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_of_a_source_whose_key_can_not_be_read_says_so_without_asking_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var unreadable = await _integrations.CreateWithUnreadableApiKey("torznab", "Unreadable");
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);

        //Act
        var ownerResponse = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));
        var memberResponse = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var forOwner = Assert.Single((await ReadAsync(ownerResponse, cancellationToken)).Sources, source => source.IntegrationId == unreadable.Id);
        Assert.Null(forOwner.Status);
        Assert.Equal(ReleaseSourceNotSearchedReason.SecretUnreadable, forOwner.NotSearchedReason);
        Assert.Equal(
            "The stored API key of “Unreadable” can't be read with this installation's encryption key. Enter the API key again and save.",
            forOwner.Error);
        Assert.Empty(forOwner.Releases);
        var forMember = Assert.Single((await ReadAsync(memberResponse, cancellationToken)).Sources, source => source.IntegrationId == unreadable.Id);
        Assert.Equal(ReleaseSourceNotSearchedReason.SecretUnreadable, forMember.NotSearchedReason);
        Assert.Null(forMember.Error);
        // Only the readable source was asked.
        Assert.All(_factory.TorznabHttp.Requests, request => Assert.StartsWith("/1/", request.Uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_of_a_source_whose_type_this_version_can_not_search_says_so()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var orphan = await _integrations.Create("gone-type", "Orphan", kind: IntegrationKind.ReleaseSearch);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(orphan.Id, source.IntegrationId);
        Assert.Null(source.Status);
        Assert.Equal(ReleaseSourceNotSearchedReason.TypeUnavailable, source.NotSearchedReason);
        Assert.Equal("This version of the app can't search releases through “gone-type”.", source.Error);
    }

    [Fact]
    public async Task Search_of_a_source_that_offers_no_search_the_app_can_use_says_so_without_searching()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.CapsXml = """<caps><searching><search available="no" /><movie-search available="no" /></searching></caps>""";

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Null(source.Status);
        Assert.Equal(ReleaseSourceNotSearchedReason.NoUsableSearch, source.NotSearchedReason);
        Assert.Equal(
            "“Toloka” offers no search this app can use: no search by text, and no movie or TV search by title or by an id that was asked for.",
            source.Error);
        // The capabilities were read; nothing was searched.
        Assert.Single(_factory.TorznabHttp.Requests);
    }

    [Fact]
    public async Task Search_of_a_source_whose_answer_the_app_can_not_read_keeps_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        // Not a transport failure the adapter knows: something nobody foresaw, as a parser bug would be.
        _toloka.Intercept = _ => throw new InvalidOperationException($"Unexpected answer quoting {ApiKey}");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        var toloka = Assert.Single(sources, source => source.Name == "Toloka");
        Assert.Equal(IntegrationCallStatus.UnexpectedResponse, toloka.Status);
        Assert.Equal("The app couldn't read what “Toloka” answered.", toloka.Error);
        Assert.Equal(SampleMovieCount, Assert.Single(sources, source => source.Name == "Other").Releases.Count);
        // Logged as an error with the exception's type and stack, never its message: that can quote what
        // the service sent.
        var logged = Assert.Single(_factory.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("System.InvalidOperationException", logged.Message, StringComparison.Ordinal);
        Assert.Contains(" at ", logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected answer quoting", logged.Message, StringComparison.Ordinal);
        Assert.Null(logged.Exception);
        Assert.DoesNotContain(ApiKey, logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_of_a_source_removed_while_it_was_searched_leaves_it_and_its_releases_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        // The owner removes Toloka while its search is on the wire.
        _toloka.Intercept = request =>
        {
            if (request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal))
            {
                _factory.WithDbAsync(db => db.IntegrationConnections.Where(connection => connection.Id == toloka.Id).ExecuteDeleteAsync())
                    .GetAwaiter().GetResult();
            }

            return null;
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Other", Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Name);
        await _factory.WithDbAsync(async db =>
            Assert.All(await db.Releases.AsNoTracking().ToListAsync(cancellationToken), release => Assert.Equal("Other", release.SourceName)));
    }

    [Fact]
    public async Task Search_stores_a_source_renamed_while_it_was_searched_under_its_new_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = request =>
        {
            if (request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal))
            {
                _factory.WithDbAsync(db => db.IntegrationConnections.Where(connection => connection.Id == toloka.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(connection => connection.Name, "Toloka.to")))
                    .GetAwaiter().GetResult();
            }

            return null;
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        Assert.Equal("Toloka.to", Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Name);
        await _factory.WithDbAsync(async db =>
            Assert.All(await db.Releases.AsNoTracking().ToListAsync(cancellationToken), release => Assert.Equal("Toloka.to", release.SourceName)));
    }

    [Fact]
    public async Task Search_asks_the_sources_at_once_rather_than_one_after_another()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        // Each feed's caps wait until both feeds have been asked: one after the other, the first would
        // wait in vain, give up, and fail its source.
        using var bothAsked = new CountdownEvent(2);
        Func<HttpRequestMessage, HttpResponseMessage?> meet = request =>
        {
            if (request.RequestUri!.Query.Contains("t=caps", StringComparison.Ordinal))
            {
                bothAsked.Signal();
                if (!bothAsked.Wait(TimeSpan.FromSeconds(10), cancellationToken))
                    throw new TimeoutException("The other source was never asked.");
            }

            return null;
        };
        _toloka.Intercept = meet;
        _other.Intercept = meet;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new ReleaseSearchRequest("Interstellar"));

        //Assert
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        Assert.Equal(2, sources.Count);
        Assert.All(sources, source => Assert.Equal(IntegrationCallStatus.Succeeded, source.Status));
    }

    private static async Task<ReleaseSearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ReleaseSearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
