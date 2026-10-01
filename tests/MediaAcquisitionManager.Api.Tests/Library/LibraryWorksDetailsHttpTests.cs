using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-8 — what one work of the Library page carries: its titles, year and identifiers with who claimed
// them, the schemes it lacks, its versions with their files — each file's path mapped into the app
// through the integration's mappings as they are now (ADR 0003), never stored — and its library. A value
// the library didn't report is null, never a guess.
public sealed class LibraryWorksDetailsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IIntegrationConnectionSeeder _integrations = null!;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;
    private FakeJellyfinServer _jellyfin = null!;

    public LibraryWorksDetailsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    /// <summary>A path mapping of the integration (none when null), and how the Alien. Collection files
    /// then map: the status, and the app path's folder (none when null).</summary>
    public static TheoryData<string?, string?, PathMapStatus, string?> Mappings => new()
    {
        // No mapping: Jellyfin sees the files where the app does.
        { null, null, PathMapStatus.Identity, "/media/movies/Alien. Collection" },
        { "/media/movies", "/data/movies", PathMapStatus.Mapped, "/data/movies/Alien. Collection" },
        // A mapping that covers something else: no guess, the reason instead.
        { "/media/tv-shows", "/data/tv", PathMapStatus.NotCovered, null },
    };

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
    public async Task Works_gives_a_work_its_titles_identifiers_versions_and_files()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        var syncedAt = _factory.Time.GetUtcNow().UtcDateTime;
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        var fileIds = await FileIdsAsync(cancellationToken);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal((MediaWorkKind.Movie, "Чужі", "Aliens", 1986), (work.Kind, work.Title, work.OriginalTitle, work.Year));
        // Both items claim both ids; a claim is its scheme, value and source, so each shows once.
        Assert.Equal(
            [
                new LibraryIdentifierDto(MediaIdentifierScheme.Imdb, "tt0090605", "Living room"),
                new LibraryIdentifierDto(MediaIdentifierScheme.Tmdb, "679", "Living room"),
            ],
            work.Identifiers);
        Assert.Empty(work.MissingIdentifiers);
        // Two versions of one work, each with its file where Jellyfin has it, and its library.
        Assert.Equal(
            [
                (JellyfinSamples.AliensTheatricalId, "Living room", null, syncedAt,
                    new LibraryFileDto(fileIds[AliensTheatricalPath], AliensTheatricalPath, AliensTheatricalPath, PathMapStatus.Identity, null, 14_845_708_473, "Фільми", null)),
                (JellyfinSamples.AliensSpecialEditionId, "Living room", null, syncedAt,
                    new LibraryFileDto(fileIds[AliensSpecialEditionPath], AliensSpecialEditionPath, AliensSpecialEditionPath, PathMapStatus.Identity, null, 19_620_073_340, "Фільми", null)),
            ],
            work.Versions
                .OrderBy(version => version.SourceItemId, StringComparer.Ordinal)
                .Select(version => (version.SourceItemId, version.SourceName, version.EditionLabel, version.ObservedUtc, Assert.Single(version.Files))));
    }

    [Fact]
    public async Task Works_gives_the_versions_of_one_item_their_edition_labels()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Clear();
        _jellyfin.Movies.Add(JellyfinSamples.AliensWithBothSources());
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        // Jellyfin's version picker labels: what tells two versions of one item apart.
        var work = Assert.Single((await ReadListAsync(response, cancellationToken)).Items);
        Assert.Equal(
            [
                ("[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom]", AliensTheatricalPath),
                ("[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA]", AliensSpecialEditionPath),
            ],
            work.Versions
                .Select(version => (version.EditionLabel!, Assert.Single(version.Files).ServicePath))
                .OrderBy(version => version.Item2, StringComparer.Ordinal));
        Assert.All(work.Versions, version => Assert.Equal(JellyfinSamples.AliensTheatricalId, version.SourceItemId));
    }

    [Fact]
    public async Task Works_leaves_every_value_the_library_did_not_report_unknown()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _jellyfin.Movies.Clear();
        // An original title only; no year, no provider ids; one file with no size, outside every library.
        _jellyfin.Movies.Add(new JsonObject
        {
            ["Id"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ["Type"] = "Movie",
            ["OriginalTitle"] = "Nameless",
            ["MediaSources"] = new JsonArray(new JsonObject
            {
                ["Id"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                ["Protocol"] = "File",
                ["Path"] = "/srv/elsewhere/nameless.mkv",
            }),
        });
        // A title and a year, and no media source at all.
        _jellyfin.Movies.Add(new JsonObject
        {
            ["Id"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ["Type"] = "Movie",
            ["Name"] = "Без файлу",
            ["ProductionYear"] = 2001,
        });
        // A stream: a version Jellyfin plays from an address, with no file of its own.
        _jellyfin.Movies.Add(new JsonObject
        {
            ["Id"] = "cccccccccccccccccccccccccccccccc",
            ["Type"] = "Movie",
            ["Name"] = "Стрім",
            ["MediaSources"] = new JsonArray(new JsonObject
            {
                ["Id"] = "cccccccccccccccccccccccccccccccc",
                ["Protocol"] = "Http",
                ["IsRemote"] = true,
                ["Path"] = "http://stream.test/movie",
            }),
        });
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _owner.AccessToken);

        //Assert
        var works = (await ReadListAsync(response, cancellationToken)).Items;
        Assert.Equal(3, works.Count);
        var nameless = works.Single(work => work.OriginalTitle == "Nameless");
        Assert.Equal((null, null), (nameless.Title, nameless.Year));
        Assert.Empty(nameless.Identifiers);
        Assert.Equal([MediaIdentifierScheme.Imdb, MediaIdentifierScheme.Tmdb], nameless.MissingIdentifiers);
        var file = Assert.Single(Assert.Single(nameless.Versions).Files);
        Assert.Equal(("/srv/elsewhere/nameless.mkv", null, null), (file.ServicePath, file.SizeBytes, file.LocationName));

        var withoutFile = works.Single(work => work.Title == "Без файлу");
        Assert.Equal((null, 2001), (withoutFile.OriginalTitle, withoutFile.Year));
        Assert.Empty(withoutFile.Versions);

        var stream = works.Single(work => work.Title == "Стрім");
        Assert.Null(stream.Year);
        Assert.Empty(Assert.Single(stream.Versions).Files);
    }

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Works_maps_each_file_through_its_integrations_path_mappings(
        string? servicePrefix, string? appPrefix, PathMapStatus status, string? appFolder)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create(
            "jellyfin",
            "Living room",
            apiKey: ApiKey,
            pathMappings: servicePrefix is null ? [] : [new PathMappingPair(servicePrefix, appPrefix!)]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _owner.AccessToken);

        //Assert
        var files = Assert.Single((await ReadListAsync(response, cancellationToken)).Items).Versions.SelectMany(version => version.Files).ToList();
        Assert.Equal(2, files.Count);
        Assert.All(files, file =>
        {
            Assert.Equal(status, file.PathStatus);
            var fileName = file.ServicePath[(file.ServicePath.LastIndexOf('/') + 1)..];
            Assert.Equal(appFolder is null ? null : $"{appFolder}/{fileName}", file.AppPath);
            // A reason only when there is no app path, and it names the path it couldn't map.
            Assert.Equal(
                appFolder is null ? $"No path mapping of this integration covers “{file.ServicePath}”." : null,
                file.PathProblem);
        });
    }

    [Fact]
    public async Task Works_as_a_signed_in_non_owner_gives_a_file_without_an_app_path_its_status_but_not_why()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // A mapping that covers something else: neither file of "Чужі" gets an app path.
        var jellyfin = await _integrations.Create(
            "jellyfin", "Living room", apiKey: ApiKey, pathMappings: [new PathMappingPair("/media/tv-shows", "/data/tv")]);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _member.AccessToken);

        //Assert
        // MAM-8 — the reason quotes the owner's path mappings, as the sources list's does, so it is the
        // owner's (Works_maps_each_file_through_its_integrations_path_mappings); the status is everyone's.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("No path mapping", body, StringComparison.Ordinal);
        var files = Assert.Single((await ReadListAsync(response, cancellationToken)).Items).Versions.SelectMany(version => version.Files).ToList();
        Assert.Equal(
            [(AliensTheatricalPath, PathMapStatus.NotCovered, null, null), (AliensSpecialEditionPath, PathMapStatus.NotCovered, null, null)],
            files
                .Select(file => (file.ServicePath, file.PathStatus, file.AppPath, file.PathProblem))
                .OrderBy(file => file.ServicePath, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Works_maps_paths_through_the_mappings_as_they_are_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await _integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        var before = await ReadListAsync(await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _owner.AccessToken), cancellationToken);
        // The owner adds a mapping after the sync: no new sync needed.
        await _factory.PutHttpRequest(
            $"/api/settings/integrations/{jellyfin.Id}",
            _owner.AccessToken,
            new UpdateIntegrationRequest("Living room", jellyfin.BaseUrl, PathMappings: [new PathMappingRequest("/media/movies", "/data/movies")]));

        //Act
        var response = await _factory.GetHttpRequest($"{BaseUrl}?search=чужі", _owner.AccessToken);

        //Assert
        // ADR 0003 — app paths are worked out when read, never stored.
        Assert.All(before.Items.Single().Versions.SelectMany(version => version.Files), file => Assert.Equal(PathMapStatus.Identity, file.PathStatus));
        var files = Assert.Single((await ReadListAsync(response, cancellationToken)).Items).Versions.SelectMany(version => version.Files);
        Assert.Equal(
            [
                (PathMapStatus.Mapped, "/data/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv"),
                (PathMapStatus.Mapped, "/data/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv"),
            ],
            files.Select(file => (file.PathStatus, file.AppPath!)).OrderBy(file => file.Item2, StringComparer.Ordinal));
        await _factory.WithDbAsync(async db =>
            Assert.Equal(
                [AliensTheatricalPath, AliensSpecialEditionPath],
                (await db.PhysicalFiles.Select(file => file.ServicePath).ToListAsync(cancellationToken))
                    .Where(path => path.Contains("Alien. Collection", StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)));
    }

    // Every stored file's id by its path.
    private async Task<Dictionary<string, Guid>> FileIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new Dictionary<string, Guid>();
        await _factory.WithDbAsync(async db =>
            ids = await db.PhysicalFiles.ToDictionaryAsync(file => file.ServicePath, file => file.Id, cancellationToken));
        return ids;
    }

    private static async Task<LibraryWorkListResponse> ReadListAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<LibraryWorkListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
