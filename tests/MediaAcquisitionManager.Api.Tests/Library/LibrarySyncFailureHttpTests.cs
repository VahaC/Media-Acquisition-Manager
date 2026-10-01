using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Data;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8, ADR 0008 — a sync reads the whole library before it writes anything, so every way it can fail
// (Jellyfin refusing the key, too old, failing, changing while it is read; the integration removed,
// switched off or its key unreadable) fails the run with a sentence and leaves the snapshot the last
// good sync stored exactly as it was, row by row. The sentences never carry the key.
public sealed class LibrarySyncFailureHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    /// <summary>How every "the library changed while it was read" sentence ends (ADR 0008).</summary>
    private const string LibraryChangedEnding =
        "so its library changed while it was being read. Nothing was changed here; sync again once Jellyfin's library scan has finished.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibrarySyncFailureHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        _jellyfin = new FakeJellyfinServer { ApiKey = ApiKey };
        _jellyfin.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_with_a_refused_key_fails_with_its_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        _jellyfin.ApiKey = "a-key-jellyfin-has-since-revoked";

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(run, "Jellyfin refused the API key. Create one in Jellyfin under Dashboard → API Keys and enter it here.");
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_of_a_jellyfin_older_than_10_9_fails_before_asking_for_anything_else()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        _jellyfin.SystemInfo["Version"] = "10.8.13";
        var requestsBefore = _factory.JellyfinHttp.Requests.Count;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // The version gate is the connection test: nothing is read from a server nobody has seen answer.
        AssertFailed(run, "This is Jellyfin 10.8.13; the app reads libraries from Jellyfin 10.9 or later. Update Jellyfin, then test again.");
        Assert.Equal(["/System/Info"], _factory.JellyfinHttp.Requests.Skip(requestsBefore).Select(request => request.Uri.AbsolutePath));
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_whose_list_of_libraries_fails_fails_with_the_services_sentence_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        _jellyfin.Intercept = request => request.RequestUri!.AbsolutePath == "/Library/VirtualFolders"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(run, "Jellyfin answered 500 Internal Server Error. Its own log says why.");
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_whose_second_page_fails_stores_nothing_of_the_first()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        SetLibrary(120);
        _jellyfin.Intercept = request => request.RequestUri!.Query.Contains("startIndex=49&", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : null;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // A partial read never replaces anything: not the four movies stored, not the 50 just read.
        AssertFailed(run, "Jellyfin answered 500 Internal Server Error. Its own log says why.");
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_whose_total_changes_on_a_middle_page_fails_with_the_library_changed_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        SetLibrary(120);
        _jellyfin.TotalRecordCountFor = startIndex => startIndex == 49 ? 121 : 120;

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(run, $"Jellyfin listed 120 movies when the sync began and 121 by its end, {LibraryChangedEnding}");
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_of_a_library_that_shifts_between_two_pages_fails_with_the_library_changed_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        SetLibrary(120);
        // A library scan running under the sync. Before the second page it removes a movie the first page
        // has read and adds one at the end; before the last page it adds one in the middle and removes
        // one not read yet. The total stays 120 at every page and 120 different movies come back, yet
        // read on, the sync would store the removed movie and miss the new one: only the second page not
        // starting at the first page's last movie shows the list moved.
        _jellyfin.BeforeItemsPage = startIndex =>
        {
            if (startIndex == 49)
            {
                _jellyfin.Movies.RemoveAt(10);
                _jellyfin.Movies.Add(JellyfinSamples.Renumbered(JellyfinSamples.Movie(JellyfinSamples.District13Id), 999));
            }
            else if (startIndex == 98)
            {
                _jellyfin.Movies.Insert(60, JellyfinSamples.Renumbered(JellyfinSamples.Movie(JellyfinSamples.District13Id), 998));
                _jellyfin.Movies.RemoveAt(110);
            }
        };

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(run, $"Jellyfin's list of movies shifted between two pages, {LibraryChangedEnding}");
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_whose_middle_page_comes_back_short_fails_with_the_library_changed_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        SetLibrary(120);
        // The second page lists 49 movies where 50 were asked for — one left out of its middle — while
        // Jellyfin still lists 120, and it starts and ends where it should: the boundary movie comes back
        // first, and the last page starts at its last movie. Only its being short shows a movie went
        // missing; read on, the run would fail later, for a count, with another sentence.
        _jellyfin.Intercept = request =>
        {
            if (!request.RequestUri!.Query.Contains("startIndex=49&", StringComparison.Ordinal))
                return null;
            var movies = _jellyfin.Movies.Skip(49).Take(50).Where((_, index) => index != 21).Select(movie => movie.DeepClone());
            var page = new JsonObject { ["Items"] = new JsonArray([.. movies]), ["TotalRecordCount"] = 120, ["StartIndex"] = 49 };
            return VendorResponses.Text(page.ToJsonString(), HttpStatusCode.OK, "application/json");
        };

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(run, $"Jellyfin's list of movies shifted between two pages, {LibraryChangedEnding}");
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_that_reads_fewer_different_movies_than_listed_fails_with_the_library_changed_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        SetLibrary(120);
        // One movie listed twice, away from every page boundary: totals and boundaries agree, yet only
        // 119 different movies come back.
        _jellyfin.Movies[100] = _jellyfin.Movies[10].DeepClone().AsObject();

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        Assert.Equal(LibrarySyncStatus.Failed, run.Status);
        Assert.Contains("119 different movies of the 120", run.Error, StringComparison.Ordinal);
        Assert.EndsWith(LibraryChangedEnding, run.Error, StringComparison.Ordinal);
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_during_which_the_integration_is_removed_fails_and_stores_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        SetLibrary(120);
        // The owner removes it while its second page is read. Off the test's thread: the removal is a
        // request of its own, which this handler waits for.
        _jellyfin.BeforeItemsPage = startIndex =>
        {
            if (startIndex == 49)
            {
                Task.Run(() => _factory.DeleteHttpRequest($"/api/settings/integrations/{jellyfin.Id}", _owner.AccessToken), cancellationToken)
                    .GetAwaiter()
                    .GetResult()
                    .EnsureSuccessStatusCode();
            }
        };

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        // Its snapshot went with it, and a sync that finished reading afterwards must not bring it back.
        AssertFailed(run, "The integration was removed while it was being synced; nothing was stored.");
        Assert.Equal((null, "Living room"), (run.IntegrationId, run.IntegrationName));
        Assert.Empty(await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_of_an_integration_removed_before_the_worker_took_it_fails_without_reaching_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var runId = await StartAsync(jellyfin, cancellationToken);
        await _factory.DeleteHttpRequest($"/api/settings/integrations/{jellyfin.Id}", _owner.AccessToken);

        //Act
        await _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        var run = await GetRunAsync(runId, cancellationToken);
        AssertFailed(run, "The integration was removed before the sync started.");
        Assert.Equal((null, "Living room"), (run.IntegrationId, run.IntegrationName));
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.Empty(await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_of_an_integration_switched_off_before_the_worker_took_it_fails_without_reaching_jellyfin()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await CreateSyncedJellyfinAsync(cancellationToken);
        var before = await ReadRowsAsync(cancellationToken);
        var requestsBefore = _factory.JellyfinHttp.Requests.Count;
        var runId = await StartAsync(jellyfin, cancellationToken);
        await _factory.PutHttpRequest(
            $"/api/settings/integrations/{jellyfin.Id}", _owner.AccessToken, new UpdateIntegrationRequest("Living room", jellyfin.BaseUrl, IsEnabled: false));

        //Act
        await _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);

        //Assert
        AssertFailed(await GetRunAsync(runId, cancellationToken), "“Living room” was switched off before the sync started.");
        Assert.Equal(requestsBefore, _factory.JellyfinHttp.Requests.Count);
        // Switched off keeps the last snapshot listed.
        Assert.Equal(before, await ReadRowsAsync(cancellationToken));
    }

    [Fact]
    public async Task Sync_with_an_api_key_this_installation_cant_read_fails_with_the_key_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.CreateWithUnreadableApiKey("jellyfin", "Living room");

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(
            run,
            "The stored API key of “Living room” can't be read with this installation's encryption key. Enter the API key again and save.");
        Assert.Empty(_factory.JellyfinHttp.Requests);
        Assert.DoesNotContain(jellyfin.ApiKeyEncrypted!, run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_answered_with_a_redirect_that_carries_the_key_stores_the_error_with_the_key_masked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        // In the path, which the sentence names, and not only in the query, which it never does.
        _jellyfin.Intercept = _ => VendorResponses.Redirect($"https://sso.test/{ApiKey}/sign-in?token={ApiKey}");

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(
            run,
            "The address answered with a redirect to https://sso.test/***/sign-in. Enter the address the service is really at: the app doesn't follow redirects, so your credentials never go anywhere else.");
        await AssertKeyNowhereAsync(cancellationToken);
    }

    [Fact]
    public async Task Sync_of_a_jellyfin_whose_version_carries_the_key_stores_the_error_with_the_key_masked()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.SystemInfo["Version"] = $"10.8.0-{ApiKey}";
        _jellyfin.SystemInfo["ServerName"] = $"Server {ApiKey}";

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(run, "This is Jellyfin 10.8.0-***; the app reads libraries from Jellyfin 10.9 or later. Update Jellyfin, then test again.");
        await AssertKeyNowhereAsync(cancellationToken);
    }

    [Fact]
    public async Task Sync_that_fails_logs_a_warning_with_its_status_and_never_its_sentence()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _factory.JellyfinHttp.Handler = (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError);

        //Act
        var run = await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);

        //Assert
        AssertFailed(
            run,
            "Couldn't reach Jellyfin at http://127.0.0.1:9: nothing accepted the connection. Check the address and port, and that Jellyfin is running.");
        // Warning, so it ships off the box — with the call's status, never the sentence or the address.
        var warning = Assert.Single(_factory.Logs.Entries, entry => entry.Category == typeof(LibrarySyncRunner).FullName);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal($"Library sync {run.Id} of integration {jellyfin.Id} failed (Unreachable).", warning.Message);
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    // A Jellyfin integration whose library the samples were synced into, a minute ago: the snapshot a
    // failed sync must leave alone, stored before the clock moves so a rewrite would show.
    private async Task<IntegrationConnectionEntity> CreateSyncedJellyfinAsync(CancellationToken cancellationToken)
    {
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await _factory.SyncLibraryAsync(jellyfin.Id, _owner.AccessToken, cancellationToken);
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        return jellyfin;
    }

    private void SetLibrary(int count)
    {
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.AddRange(JellyfinSamples.Library(count));
    }

    private async Task<IReadOnlyList<string>> ReadRowsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> rows = [];
        await _factory.WithDbAsync(async db => rows = (await StoredLibrarySnapshot.ReadAsync(db, cancellationToken)).Rows());
        return rows;
    }

    // The owner's start over HTTP; the queued run's id.
    private async Task<Guid> StartAsync(IntegrationConnectionEntity integration, CancellationToken cancellationToken)
    {
        using var response = await _factory.PostHttpRequest($"{BaseUrl}/sources/{integration.Id}/syncs", _owner.AccessToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Id;
    }

    private async Task<LibrarySyncRunDto> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        using var response = await _factory.GetHttpRequest($"{BaseUrl}/syncs/{runId}", _owner.AccessToken);
        return (await response.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
    }

    // Failed now, with exactly this sentence, and no counts: nothing was stored.
    private void AssertFailed(LibrarySyncRunDto run, string error)
    {
        Assert.Equal(LibrarySyncStatus.Failed, run.Status);
        Assert.Equal(error, run.Error);
        Assert.Equal(_factory.Time.GetUtcNow().UtcDateTime, run.FinishedUtc);
        Assert.Equal((null, null, null), (run.WorksCount, run.VersionsCount, run.FilesCount));
    }

    // Neither the stored run nor anything logged holds the key.
    private async Task AssertKeyNowhereAsync(CancellationToken cancellationToken)
    {
        await _factory.WithDbAsync(async db =>
            Assert.DoesNotContain(
                await db.LibrarySyncRuns.Select(run => run.Error).ToListAsync(cancellationToken),
                error => error is not null && error.Contains(ApiKey, StringComparison.Ordinal)));
        Assert.DoesNotContain(_factory.Logs.Entries, entry => entry.Message.Contains(ApiKey, StringComparison.Ordinal));
    }
}
