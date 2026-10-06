using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Seeders;

/// <inheritdoc cref="IMediaWorkSeeder"/>
public sealed class MediaWorkSeeder(IServiceProvider services) : SeederBase(services), IMediaWorkSeeder
{
    /// <summary>The library's own id of the item that reported every row.</summary>
    public const string SourceItemId = "00a49b2db2200514168431dc028f0a0b";

    private IMediaWorkDataFactory Works => new MediaWorkDataFactory(Db, Resolve<TimeProvider>());

    private IMediaVersionDataFactory Versions => new MediaVersionDataFactory(Db, Resolve<TimeProvider>());

    private IPhysicalFileDataFactory Files => new PhysicalFileDataFactory(Db, Resolve<TimeProvider>());

    // "Чужі (1986)", TMDb 679, as the reference library reports its theatrical cut
    // (VendorSamples/Jellyfin/movies-page.json).
    public async Task<MediaWorkEntity> Create(IntegrationConnectionEntity source)
    {
        var work = await Works.Create();
        await Works.AddTitle(work.Id, "Чужі", MediaTitleKind.Display, 1986, source.Id, source.Name, SourceItemId);
        await Works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, "679", source.Id, source.Name, SourceItemId);
        var version = await Versions.Create(work.Id, SourceItemId, sourceConnectionId: source.Id, sourceName: source.Name);
        await Files.Create(
            version.Id,
            "/media/movies/Alien. Collection/[02.1] Aliens (1986) [Theatrical Cut, Fundamental Collection] BDRip 1080p H.265 [2xUKR_ENG] [Hurtom].mkv",
            sourceConnectionId: source.Id,
            sourceName: source.Name);
        return work;
    }

    public async Task<MediaWorkEntity> CreateMovie(
        IntegrationConnectionEntity source,
        string title,
        int? year,
        string? tmdbId = null,
        string? imdbId = null,
        string? originalTitle = null,
        IReadOnlyList<long>? fileSizes = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var itemId = Guid.NewGuid().ToString("N");
        var work = await Works.Create();
        await Works.AddTitle(work.Id, title, MediaTitleKind.Display, year, source.Id, source.Name, itemId);
        if (originalTitle is not null)
            await Works.AddTitle(work.Id, originalTitle, MediaTitleKind.Original, year, source.Id, source.Name, itemId);
        if (tmdbId is not null)
            await Works.AddIdentifier(work.Id, MediaIdentifierScheme.Tmdb, tmdbId, source.Id, source.Name, itemId);
        if (imdbId is not null)
            await Works.AddIdentifier(work.Id, MediaIdentifierScheme.Imdb, imdbId, source.Id, source.Name, itemId);
        foreach (var (size, index) in (fileSizes ?? []).Select((size, index) => (size, index)))
            await AddFile(source, work, itemId, $"{itemId}-{index}", size);
        return work;
    }

    public async Task<MediaWorkEntity> CreateSeries(
        IntegrationConnectionEntity source,
        string title,
        int? year,
        string? tmdbTvId = null,
        string? imdbId = null,
        IReadOnlyList<int>? seasons = null,
        long? episodeSizeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var itemId = Guid.NewGuid().ToString("N");
        var series = await Works.Create(MediaWorkKind.Series);
        await Works.AddTitle(series.Id, title, MediaTitleKind.Display, year, source.Id, source.Name, itemId);
        if (tmdbTvId is not null)
            await Works.AddIdentifier(series.Id, MediaIdentifierScheme.TmdbTv, tmdbTvId, source.Id, source.Name, itemId);
        if (imdbId is not null)
            await Works.AddIdentifier(series.Id, MediaIdentifierScheme.Imdb, imdbId, source.Id, source.Name, itemId);

        foreach (var season in seasons ?? [1])
            await CreateEpisode(source, series, title, season, episodeSizeBytes);

        return series;
    }

    public async Task<MediaWorkEntity> AddEpisode(
        IntegrationConnectionEntity source,
        MediaWorkEntity series,
        int season,
        long? sizeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        var seriesTitle = await Db.MediaWorkTitles.AsNoTracking()
            .Where(title => title.MediaWorkId == series.Id)
            .Select(title => title.Text)
            .FirstAsync();
        return await CreateEpisode(source, series, seriesTitle, season, sizeBytes);
    }

    private async Task<MediaWorkEntity> CreateEpisode(
        IntegrationConnectionEntity source,
        MediaWorkEntity series,
        string seriesTitle,
        int season,
        long? sizeBytes)
    {
        var episodeItemId = Guid.NewGuid().ToString("N");
        var episode = await Works.Create(MediaWorkKind.Episode);
        await Works.AddTitle(episode.Id, $"{seriesTitle} {season}x01", MediaTitleKind.Display, null, source.Id, source.Name, episodeItemId);
        await Works.AddEpisodePosition(episode.Id, series.Id, season, 1, source.Id, source.Name, episodeItemId);
        if (sizeBytes is { } size)
            await AddFile(source, episode, episodeItemId, episodeItemId, size);
        return episode;
    }

    /// <summary>A version of <paramref name="work"/> with one file of <paramref name="sizeBytes"/>.</summary>
    private async Task AddFile(IntegrationConnectionEntity source, MediaWorkEntity work, string itemId, string versionId, long sizeBytes)
    {
        var version = await Versions.Create(work.Id, itemId, versionId, sourceConnectionId: source.Id, sourceName: source.Name);
        await Files.Create(
            version.Id,
            $"/media/{versionId}.mkv",
            sizeBytes,
            sourceConnectionId: source.Id,
            sourceName: source.Name);
    }
}
