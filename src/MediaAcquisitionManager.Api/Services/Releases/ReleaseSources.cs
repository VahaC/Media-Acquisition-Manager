using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <inheritdoc cref="IReleaseSources"/>
/// <remarks>
/// MAM-12 — moved out of <see cref="ReleaseSearchService"/> unchanged, so the stored search asks the
/// sources through the same code, with the same answers and the same log lines.
/// </remarks>
public sealed class ReleaseSources(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    ILogger<ReleaseSources> logger) : IReleaseSources
{
    public async Task<IReadOnlyList<ReleaseSourceOutcome>> SearchAsync(
        string text,
        int? year,
        IReadOnlyList<MediaIdentifier> identifiers,
        CancellationToken cancellationToken = default)
    {
        // Every database read happens before the sources are asked: the context is not safe to share
        // between the concurrent calls below. A switched-off source is left alone (MAM-7's switch).
        var integrations = await db.IntegrationConnections
            .AsNoTracking()
            .Where(integration => integration.Kind == IntegrationKind.ReleaseSearch && integration.IsEnabled)
            // The normalised name is unique, so the order is total.
            .OrderBy(integration => integration.NormalizedName)
            .ToListAsync(cancellationToken);

        // Specification §2: the sources are queried concurrently, so the slowest one sets the wait, not
        // their sum, and one that fails can't hold the others back.
        return await Task.WhenAll(
            integrations.Select(integration => SearchSourceAsync(integration, text, year, identifiers, cancellationToken)));
    }

    /// <summary>The sentence for a source whose type this build can't search through (ADR 0005).</summary>
    internal static string TypeUnavailableError(string adapterType) =>
        $"This version of the app can't search releases through “{adapterType}”.";

    /// <summary>
    /// One source, from its row to what it listed. Never throws but for the caller's cancellation: a
    /// source's failure is its own answer (ADR 0004 — "a Toloka failure shows an explicit source-specific
    /// error").
    /// </summary>
    private async Task<ReleaseSourceOutcome> SearchSourceAsync(
        IntegrationConnectionEntity integration,
        string text,
        int? year,
        IReadOnlyList<MediaIdentifier> identifiers,
        CancellationToken cancellationToken)
    {
        // Every source starts on its own: one whose call runs synchronously up to its first wait (a fake, an
        // answer already at hand) would otherwise hold the others back until it is done.
        await Task.Yield();

        if (adapters.Find<IReleaseSearchProvider>(integration.AdapterType) is not { } provider)
            return NotSearched(integration, ReleaseSourceNotSearchedReason.TypeUnavailable, TypeUnavailableError(integration.AdapterType));

        var settingsResult = settingsFactory.Create(integration);
        if (settingsResult.Settings is not { } settings)
            return NotSearched(integration, ReleaseSourceNotSearchedReason.SecretUnreadable, settingsResult.Error!);

        try
        {
            var capabilities = await provider.GetCapabilitiesAsync(settings, cancellationToken);
            if (!capabilities.IsSuccess)
                return Failed(integration, capabilities.Status, capabilities.Error!, settings);

            var queries = ReleaseSearchPlanning.For(text, year, identifiers, capabilities.Value!);
            if (queries.Count == 0)
            {
                return NotSearched(
                    integration,
                    ReleaseSourceNotSearchedReason.NoUsableSearch,
                    $"“{integration.Name}” offers no search this app can use: no search by text, and no movie search by title or by an id that was asked for.");
            }

            // The tiers in order until one lists a release (ReleaseSearchPlanning): an id search that
            // finds nothing is followed by the title, never reported as "nothing there".
            IReadOnlyList<ReleaseSearchHit> hits = [];
            foreach (var query in queries)
            {
                var found = await provider.SearchAsync(settings, query, cancellationToken);
                if (!found.IsSuccess)
                    return Failed(integration, found.Status, found.Error!, settings);
                hits = found.Value!;
                if (hits.Count > 0)
                    break;
            }

            logger.LogInformation(
                "Release search of integration {IntegrationId} ({AdapterType}) found {Count} releases.",
                integration.Id,
                integration.AdapterType,
                hits.Count);
            return new ReleaseSourceOutcome(
                integration.Id, integration.Name, IntegrationCallStatus.Succeeded, null, null, hits, capabilities.Value!.Categories);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // An adapter never throws on an expected failure; this is a bug meeting an answer nobody
            // foresaw. It costs this source its results, never the others': escaping, it would fault the
            // Task.WhenAll above and answer 500 for every source. Error, with the type and the stack —
            // the methods and lines that find the bug — but never the message: a parser's message can
            // quote what the service sent.
            logger.LogError(
                "Release search of integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                integration.Id,
                integration.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return Failed(
                integration, IntegrationCallStatus.UnexpectedResponse, $"The app couldn't read what “{integration.Name}” answered.", settings);
        }
    }

    private ReleaseSourceOutcome NotSearched(IntegrationConnectionEntity integration, ReleaseSourceNotSearchedReason reason, string error)
    {
        logger.LogInformation(
            "Release search of integration {IntegrationId} ({AdapterType}) did not ask it: {Reason}.",
            integration.Id,
            integration.AdapterType,
            reason);
        return new ReleaseSourceOutcome(integration.Id, integration.Name, null, reason, error, [], []);
    }

    private ReleaseSourceOutcome Failed(
        IntegrationConnectionEntity integration, IntegrationCallStatus status, string error, IntegrationConnectionSettings settings)
    {
        // Information: the status only — never the address or the sentence (the connection test's rule).
        logger.LogInformation(
            "Release search of integration {IntegrationId} ({AdapterType}) failed: {Status}.",
            integration.Id,
            integration.AdapterType,
            status);
        // Masked again after the adapter (ADR 0007's second pass), which also covers an adapter that
        // isn't HTTP.
        return new ReleaseSourceOutcome(
            integration.Id, integration.Name, status, null, SecretRedaction.Redact(error, settings.Secrets), [], []);
    }
}
