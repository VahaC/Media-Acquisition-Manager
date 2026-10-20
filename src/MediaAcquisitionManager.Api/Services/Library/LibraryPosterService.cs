using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Api.Services.Posters;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Storage;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibraryPosterService"/>
public sealed class LibraryPosterService(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    StorageLayout storage,
    ILogger<LibraryPosterService> logger) : ILibraryPosterService
{
    /// <summary>The folder inside the data directory the library posters are kept in: beside the catalog's
    /// (<c>posters</c>), never in it, so a library's file name can't meet a catalog's.</summary>
    public const string DirectoryName = "library-posters";

    /// <summary>
    /// What a failed fetch tells anyone but the installation owner. The library's own sentence can quote the address the
    /// owner saved — the rule of a sync run's error — and the page only shows a placeholder anyway.
    /// </summary>
    public static readonly Message FetchFailedError = Message.Of("library.poster.fetch-failed");

    public async Task<LibraryPosterResult> GetAsync(string fileName, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        // The name pattern lets no separator, dot segment or drive through, and the second check says so to anyone
        // reading this without the pattern at hand: whatever comes in, the path stays inside the posters' folder.
        if (!LibraryPosterNames.TryParse(fileName, out var sourceItemId, out var posterTag)
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            return LibraryPosterResult.NotFound;

        // A poster a library reported only: the endpoint fetches nothing a sync didn't read, so it is no proxy to the
        // library. The libraries that reported it, by the (SourceItemId, Tag) index.
        var connectionIds = await db.MediaWorkPosters
            .AsNoTracking()
            .Where(poster => poster.SourceKind == ProvenanceSourceKind.MediaLibrary && poster.SourceItemId == sourceItemId && poster.Tag == posterTag)
            .Select(poster => poster.SourceConnectionId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (connectionIds.Count == 0)
            return LibraryPosterResult.NotFound;

        var directory = Path.Combine(storage.DataDirectory, DirectoryName);
        var path = Path.Combine(directory, fileName);
        // Kept: served whatever its library's state now, as the picture it names never changes.
        if (File.Exists(path))
            return LibraryPosterResult.Served(await File.ReadAllBytesAsync(path, cancellationToken));

        // Two libraries reporting one item id and tag is as good as never; a switched-on one is asked first, the rest by
        // id, so the same one is asked every time. A removed library's rows are purged with it (the snapshot's purge), so
        // a row without one is defence in depth: nothing to ask, the same answer as no poster.
        var reporters = connectionIds.OfType<Guid>().ToList();
        var integration = await db.IntegrationConnections
            .AsNoTracking()
            .Where(connection => reporters.Contains(connection.Id))
            .OrderByDescending(connection => connection.IsEnabled)
            .ThenBy(connection => connection.Id)
            .FirstOrDefaultAsync(cancellationToken);
        // Switched off: the app never calls a library the owner turned off (the sync's and the link writer's rule), and
        // the page shows its placeholder. A type this build no longer reads has nothing to ask either.
        if (integration is not { IsEnabled: true }
            || adapters.Find<IMediaLibraryProvider>(integration.AdapterType) is not { } library)
            return LibraryPosterResult.NotFound;

        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return await FailedAsync(built.Error!, callerUserId, cancellationToken);

        // No connection test first, unlike a sync and an identification (MAM-8's version gate): a row exists only from a
        // sync that passed the gate, the call is a GET of one image, and a test per poster would double the requests of a
        // page of cards.
        var poster = await library.GetPosterAsync(settings, sourceItemId, posterTag, cancellationToken);
        if (poster.Status == IntegrationCallStatus.NotFound)
            return LibraryPosterResult.NotFound;
        if (!poster.IsSuccess)
            // Masked again with the call's secrets — a second pass after the adapter's own (the MAM-7 rule).
            return await FailedAsync(poster.Error!.Redacted(settings.Secrets), callerUserId, cancellationToken);

        await PosterFiles.KeepAsync(directory, path, poster.Value!.Content, logger, cancellationToken);
        return LibraryPosterResult.Served(poster.Value.Content);
    }

    /// <summary>A failed fetch: <paramref name="error"/> for the installation owner, <see cref="FetchFailedError"/> for
    /// everyone else. Information, not Warning: a page of posters failing at once would otherwise flood what ships off
    /// the box, and the next request asks again.</summary>
    private async Task<LibraryPosterResult> FailedAsync(Message error, Guid callerUserId, CancellationToken cancellationToken)
    {
        logger.LogInformation("A poster couldn't be fetched from the media library.");
        return LibraryPosterResult.Failed(
            await db.IsInstallationOwnerAsync(callerUserId, cancellationToken) ? error : FetchFailedError);
    }
}
