using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Posters;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Recommendations;
using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <inheritdoc cref="IRecommendationPosterService"/>
public sealed class RecommendationPosterService(
    ApplicationDbContext db,
    ICatalogConnector catalogs,
    StorageLayout storage,
    ILogger<RecommendationPosterService> logger) : IRecommendationPosterService
{
    /// <summary>The folder inside the data directory the posters are kept in.</summary>
    public const string DirectoryName = "posters";

    /// <summary>
    /// What a failed fetch tells anyone but the installation owner. The catalog's own sentence can quote the address
    /// the owner saved — the rule of a run's error and a download job's — and the page only shows a placeholder anyway.
    /// MAM-47 — a message under the name the constant had (ADR 0042).
    /// </summary>
    public static readonly Message FetchFailedError = Message.Of("recommendations.poster.fetch-failed");

    public async Task<RecommendationPosterResult> GetAsync(string fileName, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        // The name pattern lets no separator, dot segment or drive through, and the second check says so to anyone
        // reading this without the pattern at hand: whatever comes in, the path stays inside the posters' folder.
        if (!RecommendationPosterNames.IsFileName(fileName) || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            return RecommendationPosterResult.NotFound;
        // A catalog work's poster only: the endpoint fetches nothing the catalog didn't name, so it is no open proxy to
        // the image host. PosterPath has no index — a scan of a few thousand rows, small beside the image itself.
        var posterPath = "/" + fileName;
        if (!await db.CatalogWorks.AnyAsync(work => work.PosterPath == posterPath, cancellationToken))
            return RecommendationPosterResult.NotFound;

        var mediaType = RecommendationPosterNames.MediaTypeOf(fileName);
        var directory = Path.Combine(storage.DataDirectory, DirectoryName);
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
            return RecommendationPosterResult.Served(await File.ReadAllBytesAsync(path, cancellationToken), mediaType);

        var connected = await catalogs.ConnectAsync(cancellationToken);
        // Nothing to fetch it from: the same answer as no poster, as the page shows its placeholder either way.
        if (connected.Status == CatalogConnectStatus.NoCatalog)
            return RecommendationPosterResult.NotFound;
        if (connected.Connection is not { } catalog)
            return await FailedAsync(connected.Error!, callerUserId, cancellationToken);

        var poster = await catalog.Catalog.GetPosterAsync(catalog.Settings, posterPath, cancellationToken);
        if (poster.Status == IntegrationCallStatus.NotFound)
            return RecommendationPosterResult.NotFound;
        if (!poster.IsSuccess)
            // Masked again with the call's secrets — a second pass after the adapter's own (the MAM-7 rule), over the
            // message's values (ADR 0042).
            return await FailedAsync(poster.Error!.Redacted(catalog.Settings.Secrets), callerUserId, cancellationToken);

        // MAM-35 — the keep the Library's posters share (PosterFiles), logged as this service.
        await PosterFiles.KeepAsync(directory, path, poster.Value!.Content, logger, cancellationToken);
        return RecommendationPosterResult.Served(poster.Value.Content, mediaType);
    }

    /// <summary>A failed fetch: <paramref name="error"/> for the installation owner, <see cref="FetchFailedError"/> for
    /// everyone else. Information, not Warning: a page of posters failing at once would otherwise flood what ships off
    /// the box, and the next request asks again.</summary>
    private async Task<RecommendationPosterResult> FailedAsync(Message error, Guid callerUserId, CancellationToken cancellationToken)
    {
        logger.LogInformation("A poster couldn't be fetched from the metadata catalog.");
        return RecommendationPosterResult.Failed(
            await db.IsInstallationOwnerAsync(callerUserId, cancellationToken) ? error : FetchFailedError);
    }
}
