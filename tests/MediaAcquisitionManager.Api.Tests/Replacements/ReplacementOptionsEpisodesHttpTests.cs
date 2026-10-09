using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-24, ADR 0032 — what a TV release offers to replace on GET /api/replacements/options: the held episodes its title
// claims — one episode, a range within one season, whole seasons —, by season, then number, each with its versions'
// files, and no episode outside the claim; an episode two library items place in two seasons listed once, at the place
// the claim holds, with the version whose own item places it outside the claim refused (R3); a title naming no season, and
// seasons the library holds none of, each a reason of the series. The confirmation names what the release claims. Episodes
// are planted as a sync stores them — one item per place, its version the item's —, their files on the fake inventory and
// inspected by a real run.
public sealed class ReplacementOptionsEpisodesHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/options";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string YellowstoneImdbId = "tt4236770";
    private const string ShowsFolder = "/media/tv-shows";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private DestinationMappingEntity _tvShows = null!;
    private LibraryLocationEntity _shows = null!;
    private MediaWorkEntity _yellowstone = null!;
    private int _inodes;

    public ReplacementOptionsEpisodesHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _owner = await new UserSeeder(_factory.Scope).CreateAuthorizedOwner("owner");
        var integrations = new IntegrationConnectionSeeder(_factory.Scope);
        _jellyfin = await integrations.Create("jellyfin", "Living room", apiKey: ApiKey);
        _toloka = await integrations.Create("torznab", "Toloka", apiKey: ApiKey);
        var qbittorrent = new FakeQBittorrentServer();
        qbittorrent.Arm(_factory.QBittorrentHttp);
        var client = await integrations.Create(
            "qbittorrent",
            "qBittorrent",
            qbittorrent.BaseUrl,
            userName: FakeQBittorrentServer.DefaultUserName,
            password: FakeQBittorrentServer.DefaultPassword,
            pathMappings: [new PathMappingPair("/downloads", "/media")]);
        // TV Shows saves into the library folder /media/tv-shows.
        _tvShows = await new DestinationMappingSeeder(_factory.Scope).Create(client, "TV Shows", "TV Shows", isDefault: true);
        _works = new MediaWorkSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        _shows = await new LibraryLocationSeeder(_factory.Scope).Create(_jellyfin, "Серіали", ShowsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn(ShowsFolder);

        _yellowstone = await _works.CreateSeries(_jellyfin, "Yellowstone", 2018, imdbId: YellowstoneImdbId, seasons: []);
        await PlantEpisodeAsync(1, 1, "Daybreak");
        await PlantEpisodeAsync(1, 2, "Kill the Messenger");
        await PlantEpisodeAsync(2, 1, "A Thousand Acres");
        await PlantEpisodeAsync(2, 2, "New Beginnings");
        await PlantEpisodeAsync(2, 3, "Only Devils Left");
        await _factory.InspectFilesAsync(_owner.AccessToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Options_for_one_episode_lists_that_held_episode_alone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAsync("Yellowstone S02E02 1080p WEB-DL");

        //Act
        var response = await OptionsAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var options = JsonSerializer.Deserialize<ReplacementOptionsResponse>(body, MediaAcquisitionManagerApiFactory.JsonOptions)!;
        Assert.Null(options.Unavailable);
        var work = Assert.Single(options.Works);
        // The episode's parts by the names the dialog reads.
        using var document = JsonDocument.Parse(body);
        Assert.Equal(
            ["workId", "seasonNumber", "episodeNumber", "title", "copies"],
            document.RootElement.GetProperty("works")[0].GetProperty("episodes")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            (_yellowstone.Id, MediaWorkKind.Series, "Yellowstone", (int?)2018, "This release is Yellowstone (2018), season 2, episode 2", (ReplacementWorkUnavailableReason?)null),
            (work.WorkId, work.Kind, work.Title, work.Year, work.ConfirmationText, work.Unavailable));
        Assert.Empty(work.Copies);
        Assert.Equal(["Серіали"], work.LibraryNames);
        var episode = Assert.Single(work.Episodes);
        Assert.Equal(((int?)2, (int?)2, "New Beginnings"), (episode.SeasonNumber, episode.EpisodeNumber, episode.Title));
        var copy = Assert.Single(episode.Copies);
        Assert.Equal((PathOf(2, 2), (ReplacementCopyUnavailableReason?)null), (copy.File.AppPath, copy.Unavailable));
    }

    [Fact]
    public async Task Options_for_a_range_within_a_season_lists_those_episodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAsync("Yellowstone S02E01-E02 1080p WEB-DL");

        //Act
        var response = await OptionsAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal("This release is Yellowstone (2018), season 2, episodes 1–2", work.ConfirmationText);
        Assert.Equal(
            [((int?)2, (int?)1, "A Thousand Acres"), (2, 2, "New Beginnings")],
            work.Episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber, episode.Title)));
    }

    [Fact]
    public async Task Options_for_whole_seasons_lists_every_held_episode_by_season_then_number()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAsync("Yellowstone (S1-2) (2018-2019) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal("This release is Yellowstone (2018), seasons 1–2", work.ConfirmationText);
        Assert.Equal(
            [
                ((int?)1, (int?)1, PathOf(1, 1)),
                (1, 2, PathOf(1, 2)),
                (2, 1, PathOf(2, 1)),
                (2, 2, PathOf(2, 2)),
                (2, 3, PathOf(2, 3)),
            ],
            work.Episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber, Assert.Single(episode.Copies).File.AppPath)));
        Assert.All(work.Episodes.SelectMany(episode => episode.Copies), copy => Assert.Null(copy.Unavailable));
    }

    [Fact]
    public async Task Options_lists_an_episode_two_items_place_once_at_the_place_the_claim_holds_and_refuses_the_other_items_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // One episode, two items: the first places it at S01E05, the second at S02E04 — a library that disagrees with
        // itself. The release claims season 2.
        const string firstItem = "item-s01e05";
        const string secondItem = "item-s02e04";
        var ghost = await _works.AddEpisodeAt(_jellyfin, _yellowstone, "Ghost", 1, 5, firstItem);
        await _works.PlaceEpisode(_jellyfin, ghost, _yellowstone, 2, 4, secondItem);
        await _works.AddVersion(_jellyfin, ghost, firstItem, _shows, null, PathOf(1, 5));
        await _works.AddVersion(_jellyfin, ghost, secondItem, _shows, null, PathOf(2, 4));
        _factory.Files.AddFile(PathOf(1, 5), TestMedia.File(inode: 105), TestMedia.Taken());
        _factory.Files.AddFile(PathOf(2, 4), TestMedia.File(inode: 204), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        var release = await FindAsync("Yellowstone (S2) (2019) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal("This release is Yellowstone (2018), season 2", work.ConfirmationText);
        // Listed once, in season 2 — never under season 1, which the release doesn't claim.
        Assert.Equal(
            [((int?)2, (int?)1), (2, 2), (2, 3), (2, 4)],
            work.Episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)));
        var copies = work.Episodes.Single(episode => episode.WorkId == ghost.Id).Copies;
        Assert.Equal(
            new Dictionary<string, ReplacementCopyUnavailableReason?>
            {
                [PathOf(1, 5)] = ReplacementCopyUnavailableReason.OutsideTheClaim,
                [PathOf(2, 4)] = null,
            },
            copies.ToDictionary(copy => copy.File.AppPath!, copy => copy.Unavailable));
    }

    [Fact]
    public async Task Options_for_a_title_that_names_no_season_is_NoClaimedSeason_with_no_episodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAsync("Yellowstone (2018) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Null(options.Unavailable);
        var work = Assert.Single(options.Works);
        Assert.Equal(
            ((ReplacementWorkUnavailableReason?)ReplacementWorkUnavailableReason.NoClaimedSeason, "This release is Yellowstone (2018)"),
            (work.Unavailable, work.ConfirmationText));
        Assert.Empty(work.Episodes);
    }

    [Fact]
    public async Task Options_for_a_season_the_library_holds_no_episode_of_is_NoClaimedEpisodes()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var release = await FindAsync("Yellowstone (S5) (2022-2024) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            ((ReplacementWorkUnavailableReason?)ReplacementWorkUnavailableReason.NoClaimedEpisodes, "This release is Yellowstone (2018), season 5"),
            (work.Unavailable, work.ConfirmationText));
        Assert.Empty(work.Episodes);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>An episode of Yellowstone at its place, by an item of its own whose version holds one file, on the disk.</summary>
    private async Task PlantEpisodeAsync(int season, int number, string title)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var episode = await _works.AddEpisodeAt(_jellyfin, _yellowstone, title, season, number, itemId);
        await _works.AddVersion(_jellyfin, episode, itemId, _shows, null, PathOf(season, number));
        _factory.Files.AddFile(PathOf(season, number), TestMedia.File(++_inodes), TestMedia.Taken());
    }

    private static string PathOf(int season, int number) =>
        $"{ShowsFolder}/Yellowstone/Season {season}/Yellowstone S{season:00}E{number:00}.mkv";

    /// <summary>The owner's search for Yellowstone (2018) found a TV release <paramref name="title"/>d, with the show's
    /// IMDb id.</summary>
    private Task<ReleaseEntity> FindAsync(string title) =>
        _releases.CreateFoundBySearch(_owner.User, _toloka, "Yellowstone", 2018, title, ReleaseKind.Tv, imdbId: YellowstoneImdbId);

    private Task<HttpResponseMessage> OptionsAsync(ReleaseEntity release) =>
        _factory.GetHttpRequest($"{BaseUrl}?releaseId={release.Id}&destinationId={_tvShows.Id}", _owner.AccessToken);

    private static async Task<ReplacementOptionsResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
