using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-18, ADR 0021 — a name the scan found that no library reported belongs to no work: which work it is, if
// any, is not the app's to guess from a file name (ADR 0017). So it changes nothing anywhere the library is
// read — the works list (plain, searched by a word of its path, filtered by a library), the libraries list,
// each source's counts, a release search's library matches — and adds no work, version or title, even when
// its path holds a work's title words. Only the Media folders row counts it (LibrarySourcesScanHttpTests).
public sealed class FolderScanIsolationHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string WorksUrl = "/api/library/works";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string Root = "/media";

    /// <summary>The theatrical "Чужі" again, unreported: its path holds the work's title, "Aliens", and a
    /// word no library path has, "Extended".</summary>
    private const string UnreportedAliensPath = "/media/downloads/Aliens (1986) Extended Cut/Aliens (1986) Extended Cut 1080p.mkv";

    private const string ContinentalPath =
        "/media/movies/John Wick. Collection/[01] The Continental (2023) - (Season 1)/[01] The Continental (2023) - S01E01 - BDRip 1080p x265 [3xUKR_ENG] [Hurtom].mkv";

    private const string District13Path =
        "/media/movies/District 13. Dilogy (2004-2009) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom]/[01] Banlieue 13 (2004) BDRip 1080p H.265 [2xUKR_FRE] [Hurtom].mkv";

    private const string AliensTheatricalPath =
        "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv";

    private const string AliensSpecialEditionPath =
        "/media/movies/Alien. Collection/[02.2] Aliens (1986) [Special Edition] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom, UTOPIA].mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private AuthorizedUser _owner = null!;
    private AuthorizedUser _member = null!;

    public FolderScanIsolationHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        var users = new UserSeeder(_factory.Scope);
        _owner = await users.CreateAuthorizedOwner("owner");
        _member = await users.CreateAuthorized("member");
        new FakeJellyfinServer { ApiKey = ApiKey }.Arm(_factory.JellyfinHttp);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Scan_of_a_name_whose_path_holds_a_works_title_changes_nothing_the_library_shows_or_finds()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var jellyfin = await new IntegrationConnectionSeeder(_factory.Scope).Create("jellyfin", "Living room", apiKey: ApiKey);
        await new LibrarySnapshotSeeder(_factory.Scope).Sync(jellyfin, _owner.User);
        await new AllowedRootSeeder(_factory.Scope).Allow(Root);
        // The library's four files where Jellyfin says they are, each read, and one more file no library lists.
        _factory.Files.AddFile(ContinentalPath, TestMedia.File(inode: 101), TestMedia.Continental());
        _factory.Files.AddFile(District13Path, TestMedia.File(inode: 102), TestMedia.Taken());
        _factory.Files.AddFile(AliensTheatricalPath, TestMedia.File(inode: 103), TestMedia.Taken());
        _factory.Files.AddFile(AliensSpecialEditionPath, TestMedia.File(inode: 104), TestMedia.Taken());
        _factory.Files.AddFile(UnreportedAliensPath, TestMedia.File(inode: 105), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var moviesLocationId = await MoviesLocationIdAsync(cancellationToken);
        var before = await LibraryAsSeenAsync(moviesLocationId, cancellationToken);

        //Act
        // The scan, and the inspection it queues, which reads the new name too: still nothing of a work.
        var scan = await _factory.ScanFoldersAsync(_owner.AccessToken, cancellationToken);
        await _factory.RunQueuedInspectionsAsync(cancellationToken);
        var after = await LibraryAsSeenAsync(moviesLocationId, cancellationToken);

        //Assert
        Assert.Equal((FolderScanStatus.Succeeded, 5, 1), (scan.Status, scan.FilesFound, scan.FilesUnreported));
        await _factory.WithDbAsync(async db =>
        {
            // Stored and read, so what follows compares against a name the app knows all it can of.
            var name = await db.PhysicalFiles.AsNoTracking().SingleAsync(file => file.MediaVersionId == null, cancellationToken);
            Assert.Equal((UnreportedAliensPath, FileInspectionStatus.Inspected), (name.ServicePath, name.InspectionStatus!.Value));
            Assert.NotNull(name.MediaInspectionId);
        });
        Assert.All(before, answer => Assert.True(answer.Status is HttpStatusCode.OK or HttpStatusCode.Created, $"{answer.What}: {answer.Status}"));
        // The searches find what they found before: the work by its title, nothing by the path's own word.
        Assert.All(before.Where(answer => answer.What == "works searched by a title word"), answer => Assert.Single(ItemsOf(answer.Body)));
        Assert.All(before.Where(answer => answer.What == "works searched by a path word"), answer => Assert.Empty(ItemsOf(answer.Body)));
        Assert.Equal(before, after);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // Everything the library shows or finds, as each reader of it answers — the member's, then the owner's
    // view — and the counts of what a library's import makes.
    private async Task<List<(string What, HttpStatusCode Status, string Body)>> LibraryAsSeenAsync(
        Guid moviesLocationId, CancellationToken cancellationToken)
    {
        var answers = new List<(string What, HttpStatusCode Status, string Body)>();
        foreach (var user in new[] { _member, _owner })
        {
            answers.Add(await AnswerAsync("works", _factory.GetHttpRequest(WorksUrl, user.AccessToken), cancellationToken));
            answers.Add(await AnswerAsync("works searched by a title word", _factory.GetHttpRequest($"{WorksUrl}?search=aliens", user.AccessToken), cancellationToken));
            answers.Add(await AnswerAsync("works searched by a path word", _factory.GetHttpRequest($"{WorksUrl}?search=extended", user.AccessToken), cancellationToken));
            answers.Add(await AnswerAsync("works of a library", _factory.GetHttpRequest($"{WorksUrl}?locationId={moviesLocationId}", user.AccessToken), cancellationToken));
            answers.Add(await AnswerAsync("libraries", _factory.GetHttpRequest("/api/library/locations", user.AccessToken), cancellationToken));
            // The sources' own rows, without the Media folders row, which is where the scan does show.
            answers.Add(await AnswerAsync("sources", _factory.GetHttpRequest("/api/library/sources", user.AccessToken), cancellationToken, "items"));
            // A release search's library matches; the search itself is a new row each time.
            answers.Add(await AnswerAsync(
                "release search", _factory.PostHttpRequest("/api/searches", user.AccessToken, new SearchRequest("Aliens", "1986")), cancellationToken, "library"));
        }

        await _factory.WithDbAsync(async db => answers.Add((
            "counts",
            HttpStatusCode.OK,
            $"{await db.MediaWorks.CountAsync(cancellationToken)} works, {await db.MediaVersions.CountAsync(cancellationToken)} versions, "
                + $"{await db.MediaWorkTitles.CountAsync(cancellationToken)} titles")));
        return answers;
    }

    // The response's status and body, or only the body's property when one is named.
    private static async Task<(string What, HttpStatusCode Status, string Body)> AnswerAsync(
        string what, Task<HttpResponseMessage> request, CancellationToken cancellationToken, string? property = null)
    {
        using var response = await request;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return (what, response.StatusCode, property is null || !response.IsSuccessStatusCode ? body : JsonNode.Parse(body)![property]!.ToJsonString());
    }

    private static JsonArray ItemsOf(string body) => JsonNode.Parse(body)!["items"]!.AsArray();

    private async Task<Guid> MoviesLocationIdAsync(CancellationToken cancellationToken)
    {
        var response = await _factory.GetHttpRequest("/api/library/locations", _owner.AccessToken);
        var locations = (await response.Content.ReadFromJsonAsync<LibraryLocationListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        return locations.Items.Single(location => location.Name == "Фільми").Id;
    }
}
