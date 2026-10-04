using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — a sync's run as the API reports it (GET /api/library/syncs/{id}) once the worker has run it:
// for every signed-in user, its error and who started it for the owner only (LibraryMapper); what the
// run sends Jellyfin (GETs with the key in the header, pages overlapping by one) and the progress it
// reports after each page.
public sealed class LibrarySyncRunHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string UnreachableError =
        "Couldn't reach Jellyfin at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that Jellyfin is running.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibrarySyncRunHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetRun_of_a_finished_sync_is_200_with_its_outcome_and_times()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        var requested = _factory.Time.GetUtcNow().UtcDateTime;
        var start = await _factory.PostHttpRequest($"{BaseUrl}/sources/{jellyfin.Id}/syncs", _owner.AccessToken);
        _factory.Time.Advance(TimeSpan.FromSeconds(30));
        var ran = _factory.Time.GetUtcNow().UtcDateTime;
        await _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await ReadRunAsync(response, cancellationToken);
        // Four movies read of four listed, into three works (the two "Чужі" items are one), no series,
        // four versions and four files.
        Assert.Equal(
            new LibrarySyncRunDto(run.Id, jellyfin.Id, "Living room", LibrarySyncStatus.Succeeded, "owner", requested, ran, ran, 4, 4, 3, 0, 4, 4, null),
            run);
        // Counts only: never an address, a path or a title.
        var finished = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibrarySyncRunner).FullName);
        Assert.Equal(LogLevel.Information, finished.Level);
        Assert.Equal($"Library sync {run.Id} of integration {jellyfin.Id} finished: 3 works, 0 series, 4 versions, 4 files.", finished.Message);
    }

    [Fact]
    public async Task Sync_reads_jellyfin_with_gets_only_and_the_key_in_the_header()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();

        //Act
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // The version gate (the connection test), the libraries, then one page of movies, of series and
        // of episodes (MAM-32) — and nothing that writes: MAM-8 never changes anything in Jellyfin.
        var requests = _factory.JellyfinHttp.Requests;
        Assert.Equal(
            ["/System/Info", "/Library/VirtualFolders", "/Items", "/Items", "/Items"],
            requests.Select(request => request.Uri.AbsolutePath));
        Assert.Equal(
            ["includeItemTypes=Movie", "includeItemTypes=Series", "includeItemTypes=Episode"],
            requests.Skip(2).Select(request => Regex.Match(request.Uri.Query, "includeItemTypes=[A-Za-z]+").Value));
        Assert.All(requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal([$"MediaBrowser Token=\"{ApiKey}\""], request.HeaderValues("Authorization"));
            Assert.DoesNotContain(ApiKey, request.Uri.AbsoluteUri, StringComparison.Ordinal);
        });
        Assert.Contains("startIndex=0&limit=50", requests[2].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_of_a_library_of_several_pages_reads_them_overlapping_by_one_and_reports_progress_after_each()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.AddRange(JellyfinSamples.Library(120));
        // What the Library page would read while each page is asked for: a fresh context, as another
        // request has, reading the run the worker is on.
        var progress = new List<(int StartIndex, int ItemsRead, int? ItemsTotal)>();
        _jellyfin.BeforeItemsPage = startIndex =>
        {
            using var scope = _factory.Services.CreateScope();
            var running = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibrarySyncRuns
                .AsNoTracking()
                .Single(run => run.Status == LibrarySyncStatus.Running);
            progress.Add((startIndex, running.ItemsRead, running.ItemsTotal));
        };

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Each page after the first starts at the previous page's last movie, so a shift between two
        // requests shows as that movie not coming back first.
        Assert.Equal(
            ["startIndex=0&limit=50", "startIndex=49&limit=50", "startIndex=98&limit=50"],
            _factory.JellyfinHttp.Requests
                .Where(request => request.Uri.AbsolutePath == "/Items" && request.Uri.Query.Contains("includeItemTypes=Movie", StringComparison.Ordinal))
                .Select(request => Regex.Match(request.Uri.Query, "startIndex=[0-9]+&limit=[0-9]+").Value));
        // MAM-32 — the first page of every list comes first, so the total is known before the rest:
        // "Syncing… 50 of 120" once the series and episodes (none here) are counted, then 99 different
        // movies after the second page.
        Assert.Equal([(0, 0, null), (49, 50, 120), (98, 99, 120)], progress);
        // 120 movies: every four make three works (the two "Чужі" copies of a number share their ids).
        Assert.Equal(
            (LibrarySyncStatus.Succeeded, 120, 120, 90, 120, 120),
            (run.Status, run.ItemsRead, run.ItemsTotal, run.WorksCount, run.VersionsCount, run.FilesCount));
    }

    [Fact]
    public async Task GetRun_as_a_signed_in_non_owner_is_200_with_the_run_but_not_who_started_it()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        var owners = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{owners.Id}", _member.AccessToken);

        //Assert
        // Everyone reads the library, so everyone may see where its sync stands. Who started it is the
        // owner's sign-in name — only the owner starts one — so it stays the owner's.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("owner", owners.RequestedByUserName);
        Assert.Equal(owners with { RequestedByUserName = null }, await ReadRunAsync(response, cancellationToken));
    }

    [Fact]
    public async Task GetRun_of_a_failed_sync_gives_its_error_to_the_owner_only()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        _factory.JellyfinHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError);
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Act
        var owners = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{run.Id}", _owner.AccessToken);
        var members = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{run.Id}", _member.AccessToken);

        //Assert
        // MAM-8 — the sentence names the service's address, and integrations are the owner's.
        Assert.Equal(HttpStatusCode.OK, owners.StatusCode);
        var ownersRun = await ReadRunAsync(owners, cancellationToken);
        Assert.Equal((LibrarySyncStatus.Failed, UnreachableError), (ownersRun.Status, ownersRun.Error));
        Assert.Equal(HttpStatusCode.OK, members.StatusCode);
        var membersBody = await members.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("127.0.0.1", membersBody, StringComparison.Ordinal);
        Assert.DoesNotContain("reach", membersBody, StringComparison.Ordinal);
        Assert.Equal(ownersRun with { RequestedByUserName = null, Error = null }, await ReadRunAsync(members, cancellationToken));
    }

    [Fact]
    public async Task GetRun_of_an_unknown_run_is_a_bare_404()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateJellyfinAsync();
        await new LibrarySyncRunSeeder(_factory.Scope).Create(jellyfin, LibrarySyncStatus.Succeeded, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{Guid.NewGuid()}", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task GetRun_without_a_token_is_401()
    {
        //Arrange
        var jellyfin = await CreateJellyfinAsync();
        var run = await new LibrarySyncRunSeeder(_factory.Scope).Create(jellyfin, LibrarySyncStatus.Succeeded, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{run.Id}");

        //Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<IntegrationConnectionEntity> CreateJellyfinAsync() =>
        _integrations.Create("jellyfin", "Living room", FakeJellyfinServer.DefaultBaseUrl, apiKey: ApiKey);

    private static async Task<LibrarySyncRunDto> ReadRunAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
