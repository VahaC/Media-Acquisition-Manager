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

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12 — the ticket's "a failed source shows its own error while the others still render": two feeds
// on one gateway, one of them failing in each way a source can, and the sources the app can't or doesn't
// ask. A search is 201 whatever its sources answered; each source's answer is stored with the search, so
// a reload shows it again — the sentence to the installation owner only (it can name the gateway's
// address, the MAM-8 rule) — and the releases of the sources that answered are listed from what was
// stored.
public sealed class SearchesFailingSourcesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";

    private const string RefusedError =
        "The Torznab feed is refusing requests for now (error 429: Indexer is disabled till 01/10/2026 14:05:00 due to recent failures.). Try again in a few minutes.";

    private const string UnreachableError =
        $"Couldn't reach Torznab at {TolokaAddress}: nothing accepted the connection. Check the address and port, and that Torznab is running.";

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _other = null!;

    public SearchesFailingSourcesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    public async Task Create_with_one_failing_source_is_201_with_its_error_beside_the_other_sources_answer()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var other = await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        RefuseTolokasSearch();

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        Assert.Equal(
            [
                (other.Id, "Other", (IntegrationCallStatus?)IntegrationCallStatus.Succeeded, (string?)null, SampleMovieCount),
                (toloka.Id, "Toloka", IntegrationCallStatus.Refused, RefusedError, 0),
            ],
            sources.Select(source => (source.IntegrationId!.Value, source.Name, source.Status, source.Error, source.ReleaseCount)));
        Assert.Null(sources[1].NotSearchedReason);
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.SearchSources.AsNoTracking().SingleAsync(source => source.Id == sources[1].Id, cancellationToken);
            Assert.Equal((IntegrationCallStatus.Refused, RefusedError, 1), (stored.Status!.Value, stored.Error, stored.Position));
            Assert.Equal(
                [sources[0].Id],
                await db.Releases.AsNoTracking().Select(release => release.SearchSourceId!.Value).Distinct().ToListAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Releases_of_a_search_with_a_failed_source_lists_every_release_of_the_others()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        RefuseTolokasSearch();
        var search = await CreateAsync(_member, cancellationToken);
        var otherSource = search.Sources.Single(source => source.Name == "Other");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(SampleMovieCount, list.TotalCount);
        Assert.All(list.Items, release => Assert.Equal((otherSource.Id, "Other"), (release.SourceId!.Value, release.SourceName)));
        // No source was asked again for the list.
        Assert.Equal(1, _factory.TorznabHttp.Requests.Count(request => request.Uri.AbsolutePath.StartsWith("/2/", StringComparison.Ordinal) && request.Uri.Query.Contains("t=movie", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Releases_filtered_to_a_failed_source_is_an_empty_page()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        RefuseTolokasSearch();
        var search = await CreateAsync(_member, cancellationToken);
        var tolokaSource = search.Sources.Single(source => source.Name == "Toloka");

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?source={tolokaSource.Id}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadListAsync(response, cancellationToken);
        Assert.Equal(0, list.TotalCount);
        Assert.Empty(list.Items);
    }

    [Theory]
    [InlineData(IntegrationCallStatus.Refused)]
    [InlineData(IntegrationCallStatus.AuthenticationFailed)]
    [InlineData(IntegrationCallStatus.Unreachable)]
    [InlineData(IntegrationCallStatus.TimedOut)]
    [InlineData(IntegrationCallStatus.ServiceError)]
    [InlineData(IntegrationCallStatus.UnexpectedResponse)]
    public async Task Create_stores_each_way_a_source_fails_as_its_own_status_while_the_other_answers(IntegrationCallStatus failure)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _toloka.Intercept = failure switch
        {
            IntegrationCallStatus.Refused => request => IsMovieSearch(request)
                ? VendorResponses.Text("""<error code="429" description="Too many requests." />""", HttpStatusCode.TooManyRequests, "application/xml")
                : null,
            // Prowlarr's recorded answer to a wrong key, to every request: a bare 401 with no body.
            IntegrationCallStatus.AuthenticationFailed => _ => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile),
            IntegrationCallStatus.Unreachable => _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
            // A cancellation nobody asked for is the client's timeout running out.
            IntegrationCallStatus.TimedOut => _ => throw new TaskCanceledException(),
            IntegrationCallStatus.ServiceError => request => IsMovieSearch(request)
                ? VendorResponses.Text("", HttpStatusCode.InternalServerError)
                : null,
            // Not a transport failure the adapter knows: something nobody foresaw, as a parser bug would be.
            _ => _ => throw new InvalidOperationException("Unexpected answer."),
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        var toloka = Assert.Single(sources, source => source.Name == "Toloka");
        Assert.Equal((failure, 0), (toloka.Status!.Value, toloka.ReleaseCount));
        Assert.False(string.IsNullOrWhiteSpace(toloka.Error));
        var other = Assert.Single(sources, source => source.Name == "Other");
        Assert.Equal((IntegrationCallStatus.Succeeded, SampleMovieCount), (other.Status!.Value, other.ReleaseCount));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.SearchSources.AsNoTracking().SingleAsync(source => source.Name == "Toloka", cancellationToken);
            Assert.Equal((failure, toloka.Error), (stored.Status!.Value, stored.Error));
            Assert.Equal(0, await db.Releases.CountAsync(release => release.SearchSourceId == stored.Id, cancellationToken));
        });
    }

    [Fact]
    public async Task Create_as_the_owner_gets_a_failed_sources_sentence_with_its_address()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        var toloka = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal((IntegrationCallStatus.Unreachable, UnreachableError), (toloka.Status!.Value, toloka.Error));
    }

    [Fact]
    public async Task Create_as_a_member_gets_a_failed_sources_status_without_its_sentence_and_stores_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        var toloka = Assert.Single(sources, source => source.Name == "Toloka");
        Assert.Equal(IntegrationCallStatus.Unreachable, toloka.Status);
        // The sentence names the gateway's address, which is the owner's (the MAM-8 rule).
        Assert.Null(toloka.Error);
        Assert.Equal(SampleMovieCount, Assert.Single(sources, source => source.Name == "Other").ReleaseCount);
        // Kept whoever searched: whether to show it is decided on every read.
        await _factory.WithDbAsync(async db =>
            Assert.Equal(UnreachableError, (await db.SearchSources.AsNoTracking().SingleAsync(source => source.Id == toloka.Id, cancellationToken)).Error));
    }

    [Fact]
    public async Task Get_as_a_member_shows_a_failed_sources_stored_status_without_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var search = await CreateAsync(_member, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var toloka = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal((IntegrationCallStatus.Unreachable, (string?)null), (toloka.Status!.Value, toloka.Error));
    }

    [Fact]
    public async Task Get_as_the_owner_shows_a_failed_sources_stored_sentence_without_asking_it_again()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        var search = await CreateAsync(_owner, cancellationToken);
        // The gateway is back: a reload shows the search as it was, not the source as it is now.
        _toloka.Intercept = null;
        var requestsBefore = _factory.TorznabHttp.Requests.Count;

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var toloka = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal((IntegrationCallStatus.Unreachable, UnreachableError, 0), (toloka.Status!.Value, toloka.Error, toloka.ReleaseCount));
        Assert.Equal(requestsBefore, _factory.TorznabHttp.Requests.Count);
    }

    [Fact]
    public async Task Create_of_a_source_whose_answer_the_app_can_not_read_logs_the_bug_without_its_message()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new InvalidOperationException($"Unexpected answer quoting {ApiKey}");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var toloka = Assert.Single((await ReadAsync(response, cancellationToken)).Sources, source => source.Name == "Toloka");
        Assert.Equal((IntegrationCallStatus.UnexpectedResponse, "The app couldn't read what “Toloka” answered."), (toloka.Status!.Value, toloka.Error));
        // The release search's own log line (one way to ask a source): the type and the stack, never the
        // message, which can quote what the service sent.
        var logged = Assert.Single(_factory.Logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("System.InvalidOperationException", logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Unexpected answer quoting", logged.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, logged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_of_sources_the_app_can_not_ask_stores_why_without_asking_them()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var unreadable = await _integrations.CreateWithUnreadableApiKey("torznab", "Unreadable");
        var orphan = await _integrations.Create("gone-type", "Orphan", kind: IntegrationKind.ReleaseSearch);
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.CapsXml = """<caps><searching><search available="no" /><movie-search available="no" /></searching></caps>""";

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _owner.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sources = (await ReadAsync(response, cancellationToken)).Sources;
        Assert.Equal(
            [
                ("Orphan", (IntegrationCallStatus?)null, (ReleaseSourceNotSearchedReason?)ReleaseSourceNotSearchedReason.TypeUnavailable,
                    (string?)"This version of the app can't search releases through “gone-type”."),
                ("Toloka", null, ReleaseSourceNotSearchedReason.NoUsableSearch,
                    "“Toloka” offers no search this app can use: no search by text, and no movie search by title or by an id that was asked for."),
                ("Unreadable", null, ReleaseSourceNotSearchedReason.SecretUnreadable,
                    "The stored API key of “Unreadable” can't be read with this installation's encryption key. Enter the API key again and save."),
            ],
            sources.Select(source => (source.Name, source.Status, source.NotSearchedReason, source.Error)));
        Assert.All(sources, source => Assert.Equal(0, source.ReleaseCount));
        Assert.Equal([orphan.Id, unreadable.Id], sources.Where(source => source.Name != "Toloka").Select(source => source.IntegrationId!.Value));
        // Toloka's capabilities were read and nothing was searched; the other two were never asked.
        Assert.Equal([$"{TolokaAddress}?t=caps&apikey={ApiKey}"], _factory.TorznabHttp.Requests.Select(request => request.Uri.AbsoluteUri));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [
                    ("Orphan", 0, (IntegrationCallStatus?)null, (ReleaseSourceNotSearchedReason?)ReleaseSourceNotSearchedReason.TypeUnavailable),
                    ("Toloka", 1, null, ReleaseSourceNotSearchedReason.NoUsableSearch),
                    ("Unreadable", 2, null, ReleaseSourceNotSearchedReason.SecretUnreadable),
                ],
                (await db.SearchSources.AsNoTracking().OrderBy(source => source.Position).ToListAsync(cancellationToken))
                    .Select(source => (source.Name, source.Position, source.Status, source.NotSearchedReason))));
    }

    [Fact]
    public async Task Create_as_a_member_gets_why_a_source_was_not_asked_without_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.CreateWithUnreadableApiKey("torznab", "Unreadable");

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal((ReleaseSourceNotSearchedReason.SecretUnreadable, (string?)null), (source.NotSearchedReason!.Value, source.Error));
    }

    [Fact]
    public async Task Create_leaves_a_switched_off_source_out()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey, isEnabled: false);

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal("Toloka", Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Name);
        Assert.DoesNotContain(_factory.TorznabHttp.Requests, request => request.Uri.AbsolutePath.StartsWith("/2/", StringComparison.Ordinal));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(["Toloka"], await db.SearchSources.AsNoTracking().Select(source => source.Name).ToListAsync(cancellationToken)));
    }

    [Fact]
    public async Task Create_of_a_source_removed_while_it_was_searched_leaves_it_out_and_no_place_empty()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        // "Other" sorts first: removed, it leaves Toloka the first place rather than a gap before it.
        var other = await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _other.Intercept = request =>
        {
            if (IsMovieSearch(request))
            {
                _factory.WithDbAsync(db => db.IntegrationConnections.Where(connection => connection.Id == other.Id).ExecuteDeleteAsync())
                    .GetAwaiter().GetResult();
            }

            return null;
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(("Toloka", SampleMovieCount), (source.Name, source.ReleaseCount));
        await _factory.WithDbAsync(async db =>
        {
            var stored = await db.SearchSources.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(("Toloka", 0), (stored.Name, stored.Position));
            Assert.All(await db.Releases.AsNoTracking().ToListAsync(cancellationToken), release => Assert.Equal(stored.Id, release.SearchSourceId));
        });
    }

    [Fact]
    public async Task Create_stores_a_source_renamed_while_it_was_searched_under_its_new_name()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        _toloka.Intercept = request =>
        {
            if (IsMovieSearch(request))
            {
                _factory.WithDbAsync(db => db.IntegrationConnections.Where(connection => connection.Id == toloka.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(connection => connection.Name, "Toloka.to")))
                    .GetAwaiter().GetResult();
            }

            return null;
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal("Toloka.to", Assert.Single((await ReadAsync(response, cancellationToken)).Sources).Name);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("Toloka.to", (await db.SearchSources.AsNoTracking().SingleAsync(cancellationToken)).Name);
            Assert.All(await db.Releases.AsNoTracking().ToListAsync(cancellationToken), release => Assert.Equal("Toloka.to", release.SourceName));
        });
    }

    [Fact]
    public async Task Get_of_a_search_whose_source_was_removed_since_keeps_its_name_and_releases()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var toloka = await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        var search = await CreateAsync(_member, cancellationToken);
        var removed = await _factory.DeleteHttpRequest($"/api/settings/integrations/{toloka.Id}", _owner.AccessToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}", _member.AccessToken);
        var releases = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases?pageSize=100", _member.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The name was copied for this: the search still says who answered (the audit-row pattern).
        var source = Assert.Single((await ReadAsync(response, cancellationToken)).Sources);
        Assert.Equal(((Guid?)null, "Toloka", (IntegrationCallStatus?)IntegrationCallStatus.Succeeded, SampleMovieCount), (source.IntegrationId, source.Name, source.Status, source.ReleaseCount));
        var list = await ReadListAsync(releases, cancellationToken);
        Assert.Equal(SampleMovieCount, list.TotalCount);
        Assert.All(list.Items, release => Assert.Equal("Toloka", release.SourceName));
        await _factory.WithDbAsync(async db =>
            Assert.Null((await db.SearchSources.AsNoTracking().SingleAsync(cancellationToken)).IntegrationConnectionId));
    }

    [Fact]
    public async Task Create_whose_every_source_fails_is_201_with_each_failure_and_no_release()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        _toloka.Intercept = _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
        _other.Intercept = _ => VendorResponses.Recorded(TorznabSamples.WrongKeyResponseFile);
        var search = await CreateAsync(_member, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/{search.Id}/releases", _member.AccessToken);

        //Assert
        Assert.Equal(
            [IntegrationCallStatus.AuthenticationFailed, IntegrationCallStatus.Unreachable],
            search.Sources.Select(source => source.Status!.Value));
        Assert.False(search.Facets.HasUnknownAudioLanguage);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadListAsync(response, cancellationToken)).TotalCount);
        await _factory.WithDbAsync(async db => Assert.Equal(0, await db.Releases.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Prowlarr's answer for an indexer it paused after failures (ADR 0004), to Toloka's movie
    /// search; its capabilities still answer.</summary>
    private void RefuseTolokasSearch() =>
        _toloka.Intercept = request => IsMovieSearch(request)
            ? VendorResponses.Text(
                """<error code="429" description="Indexer is disabled till 01/10/2026 14:05:00 due to recent failures." />""",
                HttpStatusCode.TooManyRequests,
                "application/xml")
            : null;

    private static bool IsMovieSearch(HttpRequestMessage request) => request.RequestUri!.Query.Contains("t=movie", StringComparison.Ordinal);

    /// <summary>A search for "Interstellar" by <paramref name="user"/>, as it answered; throws unless it
    /// was created.</summary>
    private async Task<SearchResponse> CreateAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.PostHttpRequest(BaseUrl, user.AccessToken, new SearchRequest("Interstellar"));
        if (response.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"The search answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<SearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;

    private static async Task<SearchReleaseListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchReleaseListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
