using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Recommendations;

// MAM-31 — the Recommendations page says which release source failed its last call for the lists, as the Search page says
// a failed source: the live check gave Jackett a wrong key, and the page showed its old counts without a word. The
// availability check's search and the newest releases' read each count, the later answer winning; the owner reads the
// source's own sentence, with what to do, and a member its status alone; a source switched off is left out. Over the real
// Torznab adapter and Jackett's recorded refusal of a wrong key.
public sealed class RecommendationSourceFailuresHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/recommendations";

    /// <summary>The address Jackett's Copy Torznab Feed gives for its Toloka.to indexer.</summary>
    private const string JackettAddress = "http://jackett.test:9117/api/v2.0/indexers/toloka/results/torznab/";

    private const string ApiKey = "planted-jackett-key-0031";

    /// <summary>The Integrations Test's and the Search page's sentence for Jackett's error 100, spelled out.</summary>
    private const string RefusedKeyError =
        "The Torznab feed refused the API key (error 100: Invalid API Key). Copy the API key from the dashboard of the service that serves this feed, such as Jackett or Prowlarr, into this integration under Integrations.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ICatalogWorkSeeder _works = null!;
    private IRecommendationCandidateSeeder _candidates = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private FakeTorznabServer _jackett = null!;

    public RecommendationSourceFailuresHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _works = new CatalogWorkSeeder(_factory.Scope);
        _candidates = new RecommendationCandidateSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        // Jackett is the only release source.
        _toloka = await new IntegrationConnectionSeeder(_factory.Scope).Create("torznab", "Toloka", JackettAddress, apiKey: ApiKey);
        _jackett = FakeTorznabServer.Jackett(JackettAddress, ApiKey);
        _jackett.SearchXml = TorznabSamples.JackettMovieSearch(JackettAddress, ApiKey);
        _jackett.Arm(_factory.TorznabHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_after_an_availability_check_jackett_refused_names_the_source_with_its_sentence_for_the_owner_and_its_status_for_a_member()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendInterstellarAsync();
        var before = await ReadListAsync(_owner, cancellationToken);
        // Jackett's recorded answer to a wrong key, HTTP 200 and error 100, as the live check's wrong key got.
        _jackett.Intercept = _ => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Act
        var owners = await ReadListAsync(_owner, cancellationToken);
        var members = await ReadListAsync(_member, cancellationToken);

        //Assert
        Assert.Empty(before.FailedSources);
        var ownersSource = Assert.Single(owners.FailedSources);
        Assert.Equal(
            ("Toloka", IntegrationCallStatus.AuthenticationFailed, (string?)RefusedKeyError, now),
            (ownersSource.Name, ownersSource.Status, ownersSource.Error, ownersSource.FailedUtc));
        // A member sees the status, not the owner's sentence (the MAM-8 rule), as on the Search page.
        var membersSource = Assert.Single(members.FailedSources);
        Assert.Equal(("Toloka", IntegrationCallStatus.AuthenticationFailed, (string?)null), (membersSource.Name, membersSource.Status, membersSource.Error));
    }

    [Fact]
    public async Task List_after_a_read_of_the_newest_releases_jackett_refused_names_the_source()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The newest releases are read only while a catalog is connected.
        await new IntegrationConnectionSeeder(_factory.Scope).Create("tmdb", "TMDb", FakeTmdbServer.BaseUrl, apiKey: FakeTmdbServer.Token);
        FakeTmdbServer.Arm(_factory.TmdbHttp);
        _jackett.Intercept = _ => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);

        //Act
        await _factory.ReadRecommendationFeedAsync(cancellationToken);

        //Assert
        var source = Assert.Single((await ReadListAsync(_owner, cancellationToken)).FailedSources);
        Assert.Equal(("Toloka", IntegrationCallStatus.AuthenticationFailed, (string?)RefusedKeyError), (source.Name, source.Status, source.Error));
    }

    [Fact]
    public async Task List_names_the_source_no_more_once_it_answers_the_next_check()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = await RecommendInterstellarAsync();
        _jackett.Intercept = _ => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);
        var refused = await ReadListAsync(_owner, cancellationToken);
        // The owner corrected the key: the work, which nothing was stored for, is searched again at the next pass.
        _jackett.Intercept = null;

        //Act
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Assert
        Assert.Single(refused.FailedSources);
        Assert.Empty((await ReadListAsync(_owner, cancellationToken)).FailedSources);
        await _factory.WithDbAsync(async db => Assert.True(
            (await db.ReleaseAvailabilities.AsNoTracking().SingleAsync(row => row.CatalogWorkId == film.Id, cancellationToken)).Found));
    }

    [Fact]
    public async Task List_leaves_out_a_source_switched_off_since_it_failed()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await RecommendInterstellarAsync();
        _jackett.Intercept = _ => VendorResponses.Recorded(TorznabSamples.JackettWrongKeyResponseFile);
        await _factory.CheckRecommendationAvailabilityAsync(cancellationToken);

        //Act
        await _factory.WithDbAsync(async db => await db.IntegrationConnections
            .Where(integration => integration.Id == _toloka.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(integration => integration.IsEnabled, false), cancellationToken));

        //Assert
        // Switched off, it is asked nothing (MAM-7's switch): nothing of it is the lists' to fix.
        Assert.Empty((await ReadListAsync(_owner, cancellationToken)).FailedSources);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Interstellar, recommended in Movies and never checked: the next pass searches it.</summary>
    private async Task<CatalogWorkEntity> RecommendInterstellarAsync()
    {
        var film = await _works.Create(MediaWorkKind.Movie, "157336", "Інтерстеллар", new DateOnly(2014, 11, 5), originalTitle: "Interstellar");
        await _candidates.Create(film, RecommendationTab.Movies);
        return film;
    }

    private async Task<RecommendationListResponse> ReadListAsync(AuthorizedUser user, CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest($"{BaseUrl}?tab=Movies", user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecommendationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }
}
