using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-40 — the Removed copies tab's list (GET /api/replacements/removals): the removals still open — moving, in the
// quarantine, waiting for review, being put back or deleted — newest first, a page at a time, each with its names in a
// replacement's shape, the room its names in the quarantine take and what Delete for good frees by each name's link count as
// the disk says it now (ADR 0031), and which actions it offers; the room every open removal takes beside the page, and
// whether replacing and removing copies is switched off now (ADR 0033). The ended ones — restored, deleted, never moved —
// are history, not listed. A page out of range is 400 with the paging sentence.
public sealed class CopyRemovalListHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/removals";
    private const string AlienPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string AlienSrtPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.uk.srt";
    private const string HeatPath = "/media/movies/Heat (1995)/Heat (1995).mkv";
    private const string RoninPath = "/media/movies/Ronin (1998)/Ronin (1998).mkv";
    private const string TakenPath = "/media/movies/Taken (2008)/Taken (2008).mkv";
    private const long VideoSize = 3_891_719_630;
    private const long SrtSize = 52_345;

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;
    private CopyRemovalPlanter _removals = null!;

    public CopyRemovalListHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        _removals = new CopyRemovalPlanter(_factory, _harness);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_shows_the_open_removals_newest_first_with_their_names_and_the_room_they_take()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        // The oldest, in the quarantine with its subtitles; then one waiting for review whose video has another name, so
        // deleting it frees nothing; then two ended, which aren't listed.
        var alien = await _removals.PlantAsync(
            CopyRemovalState.Quarantined,
            [
                new PlantedName(ReplacementNameKind.Video, AlienPath, TestMedia.File(inode: 1)),
                new PlantedName(ReplacementNameKind.Companion, AlienSrtPath, TestMedia.File(inode: 2, sizeBytes: SrtSize)),
            ],
            createdUtc: now.AddHours(-3));
        var heat = await _removals.PlantAsync(
            CopyRemovalState.NeedsReview,
            [new PlantedName(ReplacementNameKind.Video, HeatPath, TestMedia.File(inode: 3, linkCount: 2))],
            reason: "The app stopped while putting this copy back.",
            createdUtc: now.AddHours(-2),
            title: "Heat (1995)");
        await _removals.PlantAsync(
            CopyRemovalState.Restored,
            [new PlantedName(ReplacementNameKind.Video, RoninPath, TestMedia.File(inode: 4), ReplacementNameState.Restored)],
            createdUtc: now.AddHours(-1));
        await _removals.PlantAsync(
            CopyRemovalState.NotMoved,
            [new PlantedName(ReplacementNameKind.Video, TakenPath, TestMedia.File(inode: 5), ReplacementNameState.Stayed, "Refused.")],
            createdUtc: now);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((2, 1, 25, (2 * VideoSize) + SrtSize), (list.TotalCount, list.Page, list.PageSize, list.QuarantinedBytes));
        Assert.Equal([heat.Removal.Id, alien.Removal.Id], list.Items.Select(item => item.Id));
        var (heatItem, alienItem) = (list.Items[0], list.Items[1]);
        Assert.Equal(
            (CopyRemovalState.NeedsReview, (string?)"The app stopped while putting this copy back.", "Heat (1995)", HeatPath,
                CopyRemovalPlanter.QuarantinePathOf(heat.Removal.Id, HeatPath), VideoSize, 0L, true, true),
            (heatItem.State, heatItem.Reason, heatItem.Title, heatItem.OriginalPath, heatItem.QuarantinePath, heatItem.SizeBytes, heatItem.FreesBytes,
                heatItem.CanRestore, heatItem.CanDeleteForGood));
        Assert.True(Assert.Single(heatItem.Names).OtherNames);
        Assert.Equal(
            (CopyRemovalState.Quarantined, VideoSize + SrtSize, VideoSize + SrtSize, "owner", now.AddHours(-3), DuplicateClass.SameWork),
            (alienItem.State, alienItem.SizeBytes, alienItem.FreesBytes, alienItem.RequestedByUserName, alienItem.CreatedUtc, alienItem.GroupClass));
        Assert.Equal(
            [
                new ReplacementNameDto(alien.Names[0].Id, ReplacementNameKind.Video, AlienPath, alien.Names[0].QuarantinePath, VideoSize, false, ReplacementNameState.Quarantined, null),
                new ReplacementNameDto(alien.Names[1].Id, ReplacementNameKind.Companion, AlienSrtPath, alien.Names[1].QuarantinePath, SrtSize, false, ReplacementNameState.Quarantined, null),
            ],
            alienItem.Names);
    }

    [Fact]
    public async Task List_reads_what_delete_for_good_frees_by_each_names_link_count_now()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The move read two names; the torrent's own was removed with its files since, so this one is the last.
        var heat = await _removals.PlantAsync(CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, HeatPath, TestMedia.File(inode: 3, linkCount: 2))]);
        _factory.Files.AddFile(heat.Names[0].QuarantinePath, TestMedia.File(inode: 3, linkCount: 1));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _harness.Owner.AccessToken);

        //Assert
        var item = Assert.Single((await response.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!.Items);
        Assert.Equal((VideoSize, false), (item.FreesBytes, Assert.Single(item.Names).OtherNames));
    }

    [Fact]
    public async Task List_pages_the_open_removals()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = _factory.Time.GetUtcNow().UtcDateTime;
        var older = await _removals.PlantAsync(CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, AlienPath, TestMedia.File(inode: 1))], createdUtc: now.AddHours(-1));
        await _removals.PlantAsync(CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, HeatPath, TestMedia.File(inode: 3))], createdUtc: now);

        //Act
        var second = await _factory.GetHttpRequest($"{BaseUrl}?page=2&pageSize=1", _harness.Owner.AccessToken);
        var past = await _factory.GetHttpRequest($"{BaseUrl}?page=3&pageSize=1", _harness.Owner.AccessToken);

        //Assert
        var secondPage = (await second.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal([older.Removal.Id], secondPage.Items.Select(item => item.Id));
        Assert.Equal((2, 2, 1, 2 * VideoSize), (secondPage.TotalCount, secondPage.Page, secondPage.PageSize, secondPage.QuarantinedBytes));
        var pastPage = (await past.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((0, 2, 3), (pastPage.Items.Count, pastPage.TotalCount, pastPage.Page));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_says_whether_replacing_and_removing_copies_is_switched_off_now(bool switchedOff)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _removals.PlantAsync(CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, AlienPath, TestMedia.File(inode: 1))]);
        if (switchedOff)
            await _factory.WithDbAsync(db => db.ReplacementSettings.ExecuteUpdateAsync(setters => setters.SetProperty(settings => settings.IsEnabled, false), cancellationToken));

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _harness.Owner.AccessToken);
        var past = await _factory.GetHttpRequest($"{BaseUrl}?page=2", _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (response.StatusCode, past.StatusCode));
        var list = (await response.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        // The removal's own offers stay as they are, as a replacement's page keeps them: the switch is why the page disables
        // Restore and Delete for good now, read fresh on every list — a page past the end too.
        var item = Assert.Single(list.Items);
        Assert.Equal((switchedOff, true, true), (list.SwitchedOff, item.CanRestore, item.CanDeleteForGood));
        var pastPage = (await past.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((0, switchedOff), (pastPage.Items.Count, pastPage.SwitchedOff));
    }

    [Fact]
    public async Task List_with_no_removal_is_empty()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<CopyRemovalListResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
        Assert.Equal((0, 0, 0L), (list.Items.Count, list.TotalCount, list.QuarantinedBytes));
    }

    [Theory]
    [InlineData("?page=0", "Page must be 1 or more.")]
    [InlineData("?pageSize=101", "Page size must be between 1 and 100.")]
    public async Task List_with_a_page_out_of_range_is_400_with_the_sentence(string query, string error)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl + query, _harness.Owner.AccessToken);

        //Assert
        Assert.Equal((HttpStatusCode.BadRequest, error), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
    }

    [Fact]
    public async Task List_writes_a_removal_under_exactly_the_names_the_page_reads()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await _removals.PlantAsync(CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, AlienPath, TestMedia.File(inode: 1))]);

        //Act
        var response = await _factory.GetHttpRequest(BaseUrl, _harness.Owner.AccessToken);

        //Assert
        // The frontend's CopyRemovalListResponse, CopyRemoval and ReplacementName, key for key, enums by name.
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            ["items", "totalCount", "page", "pageSize", "quarantinedBytes", "switchedOff"],
            json.RootElement.EnumerateObject().Select(property => property.Name));
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal(
            ["id", "state", "reason", "title", "groupClass", "originalPath", "quarantinePath", "sizeBytes", "freesBytes", "requestedByUserName",
                "createdUtc", "stateChangedUtc", "canRestore", "canDeleteForGood", "names"],
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal(("Quarantined", "SameWork"), (item.GetProperty("state").GetString(), item.GetProperty("groupClass").GetString()));
        Assert.EndsWith("Z", item.GetProperty("createdUtc").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            ["id", "kind", "originalPath", "quarantinePath", "sizeBytes", "otherNames", "state", "problem"],
            item.GetProperty("names")[0].EnumerateObject().Select(property => property.Name));
    }
}
