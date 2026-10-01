using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Search;

// MAM-12 — the ticket's "the library and all enabled sources are queried concurrently", proven by
// rendezvous rather than by timing: each side waits, inside its own call, until the other has started.
// Asked one after the other, the first would wait in vain, give up and fail — a source by its own
// answer, the library read by the search failing. The library side is held by the test host's command
// hook (DbCommandHook) at its first read of the titles; a source is held by its feed's t=caps. And the
// library is read on a context of its own: held at the same time as the sources' read of their rows, the
// two reads meet, where one shared context would refuse the second at once.
public sealed class SearchesConcurrencyHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/searches";
    private const string TolokaAddress = "http://prowlarr.test:9696/1/api";
    private const string OtherAddress = "http://prowlarr.test:9696/2/api";
    private const string ApiKey = "planted-torznab-key-0001";
    private const string JellyfinApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>How long one side waits for the other: far longer than a passing search takes, far
    /// shorter than the app's 15-second source timeout, so a sequential search fails here, not there.</summary>
    private static readonly TimeSpan MeetingTimeout = TimeSpan.FromSeconds(10);

    private static readonly int SampleMovieCount = TorznabSamples.ItemCount - TorznabSamples.NonMovieItemCount;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeTorznabServer _toloka = null!;
    private FakeTorznabServer _other = null!;

    public SearchesConcurrencyHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
    public async Task Create_asks_the_sources_at_once_rather_than_one_after_another()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        using var bothAsked = new CountdownEvent(2);
        Func<HttpRequestMessage, HttpResponseMessage?> meet = request =>
        {
            if (IsCaps(request))
                Meet(bothAsked, "The other source was never asked.", cancellationToken);
            return null;
        };
        _toloka.Intercept = meet;
        _other.Intercept = meet;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(["Other", "Toloka"], body.Sources.Select(source => source.Name));
        Assert.All(body.Sources, source => Assert.Equal((IntegrationCallStatus.Succeeded, SampleMovieCount), (source.Status!.Value, source.ReleaseCount)));
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(
                [IntegrationCallStatus.Succeeded, IntegrationCallStatus.Succeeded],
                await db.SearchSources.AsNoTracking().Select(source => source.Status!.Value).ToListAsync(cancellationToken));
            Assert.Equal(2 * SampleMovieCount, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_reads_the_library_while_a_source_is_being_asked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Seeded before the hook is armed: the seeders' own commands would trip it.
        var interstellar = await SyncLibraryAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        using var bothStarted = new CountdownEvent(2);
        HoldTheLibraryUntil(bothStarted, cancellationToken);
        _toloka.Intercept = request =>
        {
            if (IsCaps(request))
                Meet(bothStarted, "The library was never read while the source was asked.", cancellationToken);
            return null;
        };

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", "2014"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal((IntegrationCallStatus.Succeeded, SampleMovieCount), (Assert.Single(body.Sources).Status!.Value, body.Sources[0].ReleaseCount));
        var match = Assert.Single(body.Library);
        Assert.Equal((interstellar, MatchEvidence.TitleAndYear), (match.Work.Id, match.Evidence));
        // Both sides met: the hold was released by the source's arrival, not by its timeout.
        Assert.Equal(0, bothStarted.CurrentCount);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal(body.Id, (await db.Searches.AsNoTracking().SingleAsync(cancellationToken)).Id);
            Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(cancellationToken));
        });
    }

    [Fact]
    public async Task Create_asks_the_library_and_every_source_at_once()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interstellar = await SyncLibraryAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        await _integrations.Create("torznab", "Other", OtherAddress, apiKey: ApiKey);
        // Three parties: the library and both feeds, each waiting for the other two.
        using var allStarted = new CountdownEvent(3);
        HoldTheLibraryUntil(allStarted, cancellationToken);
        Func<HttpRequestMessage, HttpResponseMessage?> meet = request =>
        {
            if (IsCaps(request))
                Meet(allStarted, "The library or the other source was never asked.", cancellationToken);
            return null;
        };
        _toloka.Intercept = meet;
        _other.Intercept = meet;

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", "2014"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.All(body.Sources, source => Assert.Equal(IntegrationCallStatus.Succeeded, source.Status));
        Assert.Equal(2, body.Sources.Count);
        Assert.Equal(interstellar, Assert.Single(body.Library).Work.Id);
        await _factory.WithDbAsync(async db => Assert.Equal(2 * SampleMovieCount, await db.Releases.CountAsync(cancellationToken)));
    }

    [Fact]
    public async Task Create_reads_the_library_on_a_context_of_its_own_while_the_sources_rows_are_read()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Seeded before the hook is armed: the seeders' own commands would trip it.
        var interstellar = await SyncLibraryAsync();
        await _integrations.Create("torznab", "Toloka", TolokaAddress, apiKey: ApiKey);
        // Each read held, inside its command, until the other has started too. On two contexts they meet;
        // on one, EF refuses whichever starts second ("a second operation was started on this context"),
        // and the first waits in vain — whatever the order the threads run in.
        using var both = new CountdownEvent(2);
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"IntegrationConnections\"", StringComparison.Ordinal),
            _ =>
            {
                Meet(both, "The library was never read while the sources' rows were.", cancellationToken);
                return Task.CompletedTask;
            });
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"MediaWorkTitles\"", StringComparison.Ordinal),
            _ =>
            {
                Meet(both, "The sources' rows were never read while the library was.", cancellationToken);
                return Task.CompletedTask;
            });

        //Act
        var response = await _factory.PostHttpRequest(BaseUrl, _member.AccessToken, new SearchRequest("Interstellar", "2014"));

        //Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadAsync(response, cancellationToken);
        Assert.Equal(interstellar, Assert.Single(body.Library).Work.Id);
        Assert.Equal((IntegrationCallStatus.Succeeded, SampleMovieCount), (Assert.Single(body.Sources).Status!.Value, body.Sources[0].ReleaseCount));
        // Both reads met: each hold was released by the other's arrival, not by its timeout.
        Assert.Equal(0, both.CurrentCount);
        await _factory.WithDbAsync(async db => Assert.Equal(SampleMovieCount, await db.Releases.CountAsync(cancellationToken)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Holds the search's first read of the library's titles until <paramref name="meeting"/> is complete;
    /// the search fails when it never is. Nothing else a search sends names that table: the sources'
    /// reads are of the integrations, the store's writes of the searches and releases.
    /// </summary>
    private void HoldTheLibraryUntil(CountdownEvent meeting, CancellationToken cancellationToken) =>
        _factory.DbCommandHook.BeforeNextCommand(
            command => command.CommandText.Contains("\"MediaWorkTitles\"", StringComparison.Ordinal),
            _ =>
            {
                Meet(meeting, "No source was asked while the library was read.", cancellationToken);
                return Task.CompletedTask;
            });

    /// <summary>Signals this side's arrival at <paramref name="meeting"/> and waits for the others;
    /// throws when they never come, which fails the side that waited.</summary>
    private static void Meet(CountdownEvent meeting, string failure, CancellationToken cancellationToken)
    {
        meeting.Signal();
        if (!meeting.Wait(MeetingTimeout, cancellationToken))
            throw new TimeoutException(failure);
    }

    private static bool IsCaps(HttpRequestMessage request) => request.RequestUri!.Query.Contains("t=caps", StringComparison.Ordinal);

    /// <summary>"Інтерстеллар" (2014), originally "Interstellar", as "Living room" holds it; its work's id.</summary>
    private async Task<Guid> SyncLibraryAsync()
    {
        const string itemId = "1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a";
        var jellyfin = new FakeJellyfinServer { ApiKey = JellyfinApiKey };
        jellyfin.Movies.Clear();
        jellyfin.Movies.Add(JellyfinSamples.MovieWith(
            itemId, "Інтерстеллар", "Interstellar", 2014, "/media/movies/Interstellar (2014)/Interstellar.mkv", "tt0816692", "157336"));
        jellyfin.Arm(_factory.JellyfinHttp);
        var integration = await _integrations.Create("jellyfin", "Living room", apiKey: JellyfinApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(integration, _owner.User);

        var workId = Guid.Empty;
        await _factory.WithDbAsync(async db => workId = await db.MediaVersions
            .Where(version => version.SourceItemId == itemId)
            .Select(version => version.MediaWorkId)
            .SingleAsync());
        return workId;
    }

    private static async Task<SearchResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<SearchResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
