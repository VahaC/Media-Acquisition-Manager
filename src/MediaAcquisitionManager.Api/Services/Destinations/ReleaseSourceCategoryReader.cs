using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <inheritdoc cref="IReleaseSourceCategoryReader"/>
/// <remarks>
/// The release search's own way of asking a source (<see cref="ReleaseSources"/>), with its sentences: the
/// adapter registry says whether this build can search through a type (ADR 0005), and the stored secrets
/// are opened by <see cref="IIntegrationSettingsFactory"/>, the one place that does.
/// </remarks>
public sealed class ReleaseSourceCategoryReader(
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    ILogger<ReleaseSourceCategoryReader> logger) : IReleaseSourceCategoryReader
{
    public async Task<IReadOnlyList<ReleaseSourceCategories>> ReadAsync(
        IReadOnlyList<IntegrationConnectionEntity> sources, CancellationToken cancellationToken = default) =>
        // MAM-32 — every source at once, as a release search asks them: the slowest one sets the wait, not
        // their sum, and one that fails can't hold the others back.
        await Task.WhenAll(sources.Select(source => AskAsync(source, cancellationToken)));

    /// <summary>
    /// What one source lists. Never throws but for the caller's cancellation, and never touches the
    /// database, so several run at once.
    /// </summary>
    private async Task<ReleaseSourceCategories> AskAsync(IntegrationConnectionEntity source, CancellationToken cancellationToken)
    {
        // Every source starts on its own: one whose call runs synchronously up to its first wait would
        // otherwise hold the others back until it is done (ReleaseSources).
        await Task.Yield();

        // A switched-off source is left alone, the way a search leaves it (MAM-7's switch); then whether this
        // build can search through its type; then whether its stored secrets can be read; then the call.
        if (!source.IsEnabled)
            return NotAsked(source, "Disabled", $"“{source.Name}” is switched off. Switch it on under Integrations to read its sections.");
        if (adapters.Find<IReleaseSearchProvider>(source.AdapterType) is not { } provider)
            return NotAsked(source, "TypeUnavailable", ReleaseSources.TypeUnavailableError(source.AdapterType));

        var settingsResult = settingsFactory.Create(source);
        if (settingsResult.Settings is not { } settings)
            return NotAsked(source, "SecretUnreadable", settingsResult.Error!);

        try
        {
            var capabilities = await provider.GetCapabilitiesAsync(settings, cancellationToken);
            if (!capabilities.IsSuccess)
                return Failed(source, capabilities.Status, capabilities.Error!, settings);

            logger.LogInformation(
                "Section listing of integration {IntegrationId} ({AdapterType}) found {Count} categories.",
                source.Id,
                source.AdapterType,
                capabilities.Value!.Categories.Count);
            return ReleaseSourceCategories.Listed(capabilities.Value.Categories);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // An adapter never throws on an expected failure; this is a bug meeting an answer nobody
            // foresaw. It costs this source its sections, never the others': escaping, it would fault the
            // Task.WhenAll above and answer 500 for the whole list. Error, with the type and the stack, never
            // the message: a parser's message can quote what the source sent (ReleaseSources).
            logger.LogError(
                "Section listing of integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                source.Id,
                source.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return Failed(source, IntegrationCallStatus.UnexpectedResponse, $"The app couldn't read what “{source.Name}” answered.", settings);
        }
    }

    private ReleaseSourceCategories NotAsked(IntegrationConnectionEntity source, string reason, string error)
    {
        logger.LogInformation(
            "Section listing of integration {IntegrationId} ({AdapterType}) did not ask it: {Reason}.", source.Id, source.AdapterType, reason);
        return ReleaseSourceCategories.NotListed(error);
    }

    private ReleaseSourceCategories Failed(
        IntegrationConnectionEntity source, IntegrationCallStatus status, string error, IntegrationConnectionSettings settings)
    {
        // Information: the status only — never the address or the sentence (the connection test's rule).
        logger.LogInformation(
            "Section listing of integration {IntegrationId} ({AdapterType}) failed: {Status}.", source.Id, source.AdapterType, status);
        // Masked again after the adapter (ADR 0007's second pass), which also covers an adapter that isn't
        // HTTP.
        return ReleaseSourceCategories.NotListed(SecretRedaction.Redact(error, settings.Secrets)!);
    }
}
