using System.Net;
using System.Net.Http.Json;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Auth;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Integrations;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-30, D15 — the Download dialog's replacement question for a release the Search page matches through its description:
// the dialog offers what the page shows matched (ADR 0032, one rule for both), so a Ukrainian-only film whose title Jackett
// or Prowlarr left as "« » (2026) …" offers its copy, and a series offers the episodes the claim that matched names — the
// description's when it matched more strongly than the title —, or the other claim's when that one names none.
public sealed class ReplacementOptionsDescriptionHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string BaseUrl = "/api/replacements/options";
    private const string ApiKey = "0123456789abcdef0123456789abcdef";
    private const string FilmsFolder = "/media/movies";
    private const string ShowsFolder = "/media/tv-shows";
    private const string FilmTitle = "Потяг «Червона Рута»";
    private const string FilmPath = "/media/movies/Potiah Chervona Ruta (2026)/Potiah Chervona Ruta (2026).mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private IMediaWorkSeeder _works = null!;
    private IReleaseSeeder _releases = null!;
    private AuthorizedUser _owner = null!;
    private IntegrationConnectionEntity _jellyfin = null!;
    private IntegrationConnectionEntity _toloka = null!;
    private DestinationMappingEntity _movies = null!;
    private DestinationMappingEntity _tvShows = null!;
    private LibraryLocationEntity _films = null!;
    private LibraryLocationEntity _shows = null!;
    private int _inodes;

    public ReplacementOptionsDescriptionHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

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
        var destinations = new DestinationMappingSeeder(_factory.Scope);
        _movies = await destinations.Create(client, "Movies", "Movies", isDefault: true);
        _tvShows = await destinations.Create(client, "TV Shows", "TV Shows");
        _works = new MediaWorkSeeder(_factory.Scope);
        _releases = new ReleaseSeeder(_factory.Scope);
        var libraries = new LibraryLocationSeeder(_factory.Scope);
        _films = await libraries.Create(_jellyfin, "Фільми", FilmsFolder);
        _shows = await libraries.Create(_jellyfin, "Серіали", ShowsFolder);
        await new AllowedRootSeeder(_factory.Scope).Allow("/media");
        await new ReplacementSettingsSeeder(_factory.Scope).SwitchOn(FilmsFolder, ShowsFolder);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Options_for_a_film_matched_only_through_its_description_offers_its_copy()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = await _works.CreateMovie(_jellyfin, FilmTitle, 2026);
        await _works.AddVersion(_jellyfin, film, Guid.NewGuid().ToString("N"), _films, null, FilmPath);
        _factory.Files.AddFile(FilmPath, TestMedia.File(++_inodes), TestMedia.Taken());
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Jackett's real listing: "Strip Cyrillic Letters" left its title punctuation; its description is Toloka's.
        var release = await _releases.CreateFoundBySearch(
            _owner.User, _toloka, FilmTitle, 2026, "« » (2026) WEB-DL-AVC Ukr | Sub Ukr/Eng", description: "Потяг «Червона Рута» (2026) WEB-DLRip-AVC Ukr | Sub Ukr/Eng");

        //Act
        var response = await OptionsAsync(release, _movies);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await ReadAsync(response, cancellationToken);
        Assert.Null(options.Unavailable);
        var work = Assert.Single(options.Works);
        Assert.Equal(
            (film.Id, MatchEvidence.TitleAndYear, "This release is Потяг «Червона Рута» (2026)", (ReplacementWorkUnavailableReason?)null),
            (work.WorkId, work.Evidence, work.ConfirmationText, work.Unavailable));
        var copy = Assert.Single(work.Copies);
        Assert.Equal((FilmPath, (ReplacementCopyUnavailableReason?)null), (copy.File.AppPath, copy.Unavailable));
    }

    [Fact]
    public async Task Options_for_a_series_matched_more_strongly_by_its_description_offers_the_episodes_the_description_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var lanterns = await _works.CreateSeries(_jellyfin, "Lanterns", 2026, seasons: []);
        await PlantEpisodeAsync(lanterns, 1, 1, "Pilot");
        await PlantEpisodeAsync(lanterns, 1, 2, "The Lamplighter");
        await PlantEpisodeAsync(lanterns, 2, 1, "Return");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // The title's season 1 began in 2025, not the show's 2026: its title alone, which never authorises a
        // replacement. The description names the show, its year and seasons 1 and 2.
        var release = await _releases.CreateFoundBySearch(
            _owner.User,
            _toloka,
            "Lanterns",
            null,
            "Lanterns (S1) (2025) WEB-DL 1080p (S1)",
            ReleaseKind.Tv,
            description: "Ліхтарі (Сезони 1-2) / Lanterns (Seasons 1-2) (2026) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release, _tvShows);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            (lanterns.Id, MatchEvidence.TitleAndYear, "This release is Lanterns (2026), seasons 1–2", (ReplacementWorkUnavailableReason?)null),
            (work.WorkId, work.Evidence, work.ConfirmationText, work.Unavailable));
        Assert.Equal(
            [((int?)1, (int?)1, PathOf(1, 1)), (1, 2, PathOf(1, 2)), (2, 1, PathOf(2, 1))],
            work.Episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber, Assert.Single(episode.Copies).File.AppPath)));
        Assert.All(work.Episodes, episode => Assert.Null(Assert.Single(episode.Copies).Unavailable));
    }

    [Fact]
    public async Task Options_for_a_series_its_title_and_description_match_alike_offers_the_episodes_the_title_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var lanterns = await _works.CreateSeries(_jellyfin, "Lanterns", 2026, seasons: []);
        await PlantEpisodeAsync(lanterns, 1, 1, "Pilot");
        await PlantEpisodeAsync(lanterns, 2, 1, "Return");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // Both by title and year: the tie goes to the title, which names season 1 alone.
        var release = await _releases.CreateFoundBySearch(
            _owner.User,
            _toloka,
            "Lanterns",
            null,
            "Lanterns (S1) (2026) WEB-DL 1080p (S1)",
            ReleaseKind.Tv,
            description: "Ліхтарі (Сезони 1-2) / Lanterns (Seasons 1-2) (2026) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release, _tvShows);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal("This release is Lanterns (2026), season 1", work.ConfirmationText);
        Assert.Equal([((int?)1, (int?)1)], work.Episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber)));
    }

    [Fact]
    public async Task Options_for_a_series_matched_by_a_description_that_names_no_season_offers_the_episodes_the_title_names()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var lanterns = await _works.CreateSeries(_jellyfin, "Lanterns", 2026, seasons: []);
        await PlantEpisodeAsync(lanterns, 1, 1, "Pilot");
        await PlantEpisodeAsync(lanterns, 2, 1, "Return");
        await _factory.InspectFilesAsync(_owner.AccessToken, cancellationToken);
        // The title names season 2 and no year: its title alone. The description names the show and its year but no
        // season, so the release still holds the title's season 2.
        var release = await _releases.CreateFoundBySearch(
            _owner.User,
            _toloka,
            "Lanterns",
            null,
            "Lanterns (S2) WEB-DL 1080p",
            ReleaseKind.Tv,
            description: "Ліхтарі / Lanterns (2026) WEB-DL 1080p");

        //Act
        var response = await OptionsAsync(release, _tvShows);

        //Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var work = Assert.Single((await ReadAsync(response, cancellationToken)).Works);
        Assert.Equal(
            (lanterns.Id, MatchEvidence.TitleAndYear, "This release is Lanterns (2026), season 2", (ReplacementWorkUnavailableReason?)null),
            (work.WorkId, work.Evidence, work.ConfirmationText, work.Unavailable));
        Assert.Equal(
            [((int?)2, (int?)1, PathOf(2, 1))],
            work.Episodes.Select(episode => (episode.SeasonNumber, episode.EpisodeNumber, Assert.Single(episode.Copies).File.AppPath)));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>An episode of <paramref name="series"/> at its place, by an item of its own whose version holds one file,
    /// on the disk.</summary>
    private async Task PlantEpisodeAsync(MediaWorkEntity series, int season, int number, string title)
    {
        var itemId = Guid.NewGuid().ToString("N");
        var episode = await _works.AddEpisodeAt(_jellyfin, series, title, season, number, itemId);
        await _works.AddVersion(_jellyfin, episode, itemId, _shows, null, PathOf(season, number));
        _factory.Files.AddFile(PathOf(season, number), TestMedia.File(++_inodes), TestMedia.Taken());
    }

    private static string PathOf(int season, int number) =>
        $"{ShowsFolder}/Lanterns/Season {season}/Lanterns S{season:00}E{number:00}.mkv";

    private Task<HttpResponseMessage> OptionsAsync(ReleaseEntity release, DestinationMappingEntity destination) =>
        _factory.GetHttpRequest($"{BaseUrl}?releaseId={release.Id}&destinationId={destination.Id}", _owner.AccessToken);

    private static async Task<ReplacementOptionsResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ReplacementOptionsResponse>(MediaAcquisitionManagerApiFactory.JsonOptions, cancellationToken))!;
}
