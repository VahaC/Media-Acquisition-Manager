using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <inheritdoc cref="ILibrarySyncService"/>
/// <remarks>
/// Whether a type can read a library is asked of the adapter registry (<c>Find&lt;IMediaLibraryProvider&gt;</c>,
/// ADR 0005), never decided by a product name.
/// </remarks>
public sealed class LibrarySyncService(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    LibrarySyncQueue queue,
    ILibraryMapper mapper,
    TimeProvider time,
    ILogger<LibrarySyncService> logger) : ILibrarySyncService
{
    private const string QueueFullError = "Too many library syncs are waiting. Try again in a minute.";

    public async Task<LibrarySyncStartResult> StartAsync(Guid integrationId, Guid requestedByUserId, CancellationToken cancellationToken = default)
    {
        // Not a media library is the same bare 404 as no integration: this URL addresses libraries.
        var integration = await db.IntegrationConnections
            .AsNoTracking()
            .SingleOrDefaultAsync(connection => connection.Id == integrationId && connection.Kind == IntegrationKind.MediaLibrary, cancellationToken);
        if (integration is null)
            return LibrarySyncStartResult.NotFound;
        if (adapters.Find<IMediaLibraryProvider>(integration.AdapterType) is null)
            return LibrarySyncStartResult.Conflict(AdapterUnavailableError(integration.AdapterType));
        if (!integration.IsEnabled)
            return LibrarySyncStartResult.Conflict($"“{integration.Name}” is switched off. Switch it on under Integrations to sync it.");
        if (await HasActiveRunAsync(integrationId, cancellationToken))
            return AlreadySyncing(integration);

        var requestedByUserName = await db.Users
            .Where(user => user.Id == requestedByUserId)
            .Select(user => user.UserName)
            .SingleAsync(cancellationToken);
        var run = new LibrarySyncRunEntity
        {
            IntegrationConnectionId = integration.Id,
            // Copied (the audit-row pattern): the run still says what it synced after a rename or removal.
            IntegrationName = integration.Name,
            RequestedByUserId = requestedByUserId,
            RequestedByUserName = requestedByUserName,
            Status = LibrarySyncStatus.Queued,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        };
        db.LibrarySyncRuns.Add(run);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // MAM-8 — two starts can both pass the check above; the partial unique index
            // (IX_LibrarySyncRuns_OneActiveRunPerIntegration) lets only one run be active, and this one
            // lost. The integration removed in between breaks the foreign key instead, which is a 404.
            // Anything else (a full disk) is not a conflict and stays a failure.
            db.ChangeTracker.Clear();
            if (await HasActiveRunAsync(integrationId, cancellationToken))
            {
                logger.LogInformation(exception, "A concurrent request started a sync of integration {IntegrationId} first; this one changed nothing.", integrationId);
                return AlreadySyncing(integration);
            }

            if (!await db.IntegrationConnections.AnyAsync(connection => connection.Id == integrationId, cancellationToken))
                return LibrarySyncStartResult.NotFound;
            throw;
        }

        if (!queue.TryEnqueue(run.Id))
        {
            // The run stays as history of the attempt, failed with the same sentence the owner gets.
            await db.LibrarySyncRuns
                .Where(queued => queued.Id == run.Id && queued.Status == LibrarySyncStatus.Queued)
                .MarkFailedAsync(QueueFullError, time.GetUtcNow().UtcDateTime);
            return LibrarySyncStartResult.Unavailable(QueueFullError);
        }

        logger.LogInformation(
            "{UserName} started library sync {RunId} of integration {IntegrationId}.", requestedByUserName, run.Id, integration.Id);
        // Only the owner may start one, so the owner's view of it.
        return LibrarySyncStartResult.Queued(mapper.ToDto(run, forOwner: true));
    }

    public async Task<LibrarySyncRunDto?> GetRunAsync(Guid runId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var run = await db.LibrarySyncRuns.AsNoTracking().SingleOrDefaultAsync(row => row.Id == runId, cancellationToken);
        return run is null ? null : mapper.ToDto(run, forOwner: await db.IsInstallationOwnerAsync(callerUserId, cancellationToken));
    }

    public async Task<LibrarySourceListResponse> ListSourcesAsync(Guid callerUserId, CancellationToken cancellationToken = default)
    {
        var isOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        var integrations = await db.IntegrationConnections
            .AsNoTracking()
            .Include(integration => integration.PathMappings)
            .Where(integration => integration.Kind == IntegrationKind.MediaLibrary)
            // The normalised name is unique, so the order is total.
            .OrderBy(integration => integration.NormalizedName)
            .ToListAsync(cancellationToken);
        var ids = integrations.Select(integration => integration.Id).ToList();

        // Batched, no N+1: the latest and the last successful run of every listed integration in one
        // query each, each a correlated subquery the (IntegrationConnectionId, CreatedUtc) index answers.
        var latestRuns = await LatestRunsAsync(ids, succeededOnly: false, cancellationToken);
        var lastSucceededRuns = await LatestRunsAsync(ids, succeededOnly: true, cancellationToken);

        // MAM-8 — counted now with the current mappings, not stored with the run, so the warning
        // always agrees with the paths the works list shows. One query for every listed library's paths.
        var paths = (await db.PhysicalFiles
                .AsNoTracking()
                .Where(file => file.SourceKind == ProvenanceSourceKind.MediaLibrary
                    && file.SourceConnectionId != null
                    && ids.Contains(file.SourceConnectionId.Value))
                .Select(file => new { IntegrationId = file.SourceConnectionId!.Value, file.ServicePath })
                .ToListAsync(cancellationToken))
            .ToLookup(file => file.IntegrationId, file => file.ServicePath);

        var items = integrations.Select(integration =>
        {
            var pairs = integration.PathMappings
                .OrderBy(mapping => mapping.Position)
                .Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))
                .ToList();
            // In path order, so "the first problem" is the same one on every poll.
            var unmapped = paths[integration.Id]
                .Order(StringComparer.Ordinal)
                .Select(path => PathMapper.Map(path, pairs))
                .Where(path => !path.HasAppPath)
                .ToList();
            return mapper.ToDto(
                integration,
                adapters.Find(integration.AdapterType)?.Descriptor,
                canReadLibrary: adapters.Find<IMediaLibraryProvider>(integration.AdapterType) is not null,
                latestRuns.GetValueOrDefault(integration.Id) is { } latest ? mapper.ToDto(latest, isOwner) : null,
                lastSucceededRuns.GetValueOrDefault(integration.Id) is { } succeeded ? mapper.ToDto(succeeded, isOwner) : null,
                unmapped.Count,
                // MAM-8 — the reason quotes the path and the mapping; integrations are the owner's. The
                // works list keeps each file's reason to the owner by the same rule (LibraryService).
                isOwner ? unmapped.FirstOrDefault()?.Reason : null);
        });

        return new LibrarySourceListResponse([.. items]);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    // The latest run (by request time, then id) of each of the integrations; with succeededOnly, the
    // latest successful one.
    private async Task<Dictionary<Guid, LibrarySyncRunEntity>> LatestRunsAsync(
        List<Guid> integrationIds, bool succeededOnly, CancellationToken cancellationToken)
    {
        var runs = succeededOnly
            ? db.LibrarySyncRuns.AsNoTracking().Where(run => run.Status == LibrarySyncStatus.Succeeded)
            : db.LibrarySyncRuns.AsNoTracking();
        var latest = await runs
            .Where(run => run.IntegrationConnectionId != null
                && integrationIds.Contains(run.IntegrationConnectionId.Value)
                && run.Id == runs
                    .Where(other => other.IntegrationConnectionId == run.IntegrationConnectionId)
                    .OrderByDescending(other => other.CreatedUtc)
                    .ThenByDescending(other => other.Id)
                    .Select(other => other.Id)
                    .First())
            .ToListAsync(cancellationToken);
        return latest.ToDictionary(run => run.IntegrationConnectionId!.Value);
    }

    private Task<bool> HasActiveRunAsync(Guid integrationId, CancellationToken cancellationToken) =>
        db.LibrarySyncRuns.AnyAsync(
            run => run.IntegrationConnectionId == integrationId
                && (run.Status == LibrarySyncStatus.Queued || run.Status == LibrarySyncStatus.Running),
            cancellationToken);

    private static LibrarySyncStartResult AlreadySyncing(IntegrationConnectionEntity integration) =>
        LibrarySyncStartResult.Conflict($"“{integration.Name}” is already being synced.");

    /// <summary>The sentence for an integration whose type this build can't read a library from; the
    /// runner fails a run with it too.</summary>
    internal static string AdapterUnavailableError(string adapterType) =>
        $"This version of the app can't read a library from “{adapterType}”.";
}
