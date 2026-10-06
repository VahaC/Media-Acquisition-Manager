using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8's acceptance, end to end over real HTTP with only Jellyfin's network faked by the real samples
// (VendorSamples, MAM-6): the owner starts a sync, the worker runs it, and a movie in a Collection
// folder is found where it is — both "Чужі" versions under one work, their paths verbatim.
public sealed class LibraryAcceptanceHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;

    public LibraryAcceptanceHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Sync_of_the_samples_then_search_finds_the_collection_folder_movies_where_they_are()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);

        //Act
        var start = await _factory.PostHttpRequest($"{BaseUrl}/sources/{jellyfin.Id}/syncs", _owner.AccessToken);
        var queued = await start.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        var requestsBeforeTheWorker = _factory.JellyfinHttp.Requests.Count;
        await _factory.Resolve<LibrarySyncWorker>().RunQueuedAsync(cancellationToken);
        var run = await _factory.GetHttpRequest(start.Headers.Location!.AbsolutePath, _owner.AccessToken);
        var works = await _factory.GetHttpRequest($"{BaseUrl}/works?search=collection", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Equal($"{BaseUrl}/syncs/{queued!.Id}", start.Headers.Location.AbsolutePath);
        Assert.Equal(LibrarySyncStatus.Queued, queued.Status);
        // The start only queues: nothing reaches Jellyfin before the worker runs, and the sync only reads —
        // MAM-34's link writes included, which only a link's save, Retry or Write again sends.
        Assert.Equal(0, requestsBeforeTheWorker);
        Assert.All(_factory.JellyfinHttp.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));

        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var finished = await run.Content.ReadFromJsonAsync<LibrarySyncRunDto>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        Assert.Equal(LibrarySyncStatus.Succeeded, finished!.Status);
        Assert.Null(finished.Error);
        // Four movies read into three works (the two "Чужі" items share their ids), four versions, four files.
        Assert.Equal((4, 4, 3, 4, 4), (finished.ItemsRead, finished.ItemsTotal, finished.WorksCount, finished.VersionsCount, finished.FilesCount));

        Assert.Equal(HttpStatusCode.OK, works.StatusCode);
        var list = await works.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken);
        // "Collection" is in the folder names "Alien. Collection" and "John Wick. Collection" only; the
        // titles come in Ukrainian alphabetical order, Cyrillic before Latin.
        Assert.Equal(2, list!.TotalCount);
        Assert.Equal(["Чужі", "The Continental"], list.Items.Select(work => work.Title));
        var aliens = list.Items[0];
        Assert.Equal(("Aliens", 1986), (aliens.OriginalTitle, aliens.Year));
        Assert.Empty(aliens.MissingIdentifiers);
        Assert.Equal(
            [AliensTheatricalPath, AliensSpecialEditionPath],
            aliens.Versions.Select(version => Assert.Single(version.Files).ServicePath).Order(StringComparer.Ordinal));
        Assert.All(aliens.Versions, version =>
        {
            Assert.Equal("Living room", version.SourceName);
            // No path mappings: Jellyfin sees the files where the app does.
            var file = Assert.Single(version.Files);
            Assert.Equal((PathMapStatus.Identity, file.ServicePath, "Фільми"), (file.PathStatus, file.AppPath, file.LocationName));
        });
        Assert.Equal([MediaIdentifierScheme.Tmdb], list.Items[1].MissingIdentifiers);
    }
}
