using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;
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
    public Task<IReadOnlyList<ReleaseSourceOutcome>> SearchAsync(
        string text,
        int? year,
        int? season,
        IReadOnlyList<MediaIdentifier> identifiers,
        CancellationToken cancellationToken = default) =>
        AskEachSourceAsync(
            // MAM-32 — the movie family, then the TV family (ADR 0020): one title may be either, and a
            // search can't tell which. A season is a show's, so a search for one asks for no movie.
            searchable => season is null
                ? [ReleaseSearchPlanning.For(text, year, identifiers, searchable), ReleaseSearchPlanning.ForTv(text, null, identifiers, searchable)]
                : [ReleaseSearchPlanning.ForTv(text, season, identifiers, searchable)],
            name => NoUsableSearchError(name, season),
            cancellationToken);

    public Task<IReadOnlyList<ReleaseSourceOutcome>> SearchKindAsync(
        string text, int? year, ReleaseKind kind, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a release kind.");

        // MAM-33 — the kind is the catalog's, so only its family is asked. By the title alone, no id: an id tier that
        // finds the few releases an indexer tagged with the id ends the tiers (ReleaseSearchPlanning), and the title's
        // would never count the rest; the catalog's ids still match what the releases list.
        return AskEachSourceAsync(
            searchable => kind == ReleaseKind.Tv
                ? [ReleaseSearchPlanning.ForTv(text, null, [], searchable)]
                : [ReleaseSearchPlanning.For(text, year, [], searchable)],
            // MAM-47 — no sentence: the recommendations keep only the sources they asked (RecommendationSourceFailures), so a
            // source this search couldn't use reads to nobody, and its sentence was dead (design §10.1).
            _ => null,
            cancellationToken);
    }

    public Task<IReadOnlyList<ReleaseSourceOutcome>> LatestAsync(bool movies, bool tv, CancellationToken cancellationToken = default) =>
        AskEachSourceAsync(
            // One query, one tier: the newest releases are a listing, not a search a later tier could widen.
            searchable => ReleaseSearchPlanning.ForLatest(movies, tv, searchable) is { } latest ? [[latest]] : [],
            // MAM-47 — no sentence, as for a search of one kind: the feed logs why, and nobody reads it.
            _ => null,
            cancellationToken);

    /// <summary>The sentence for a source whose type this build can't search through (ADR 0005).</summary>
    internal static Message TypeUnavailableError(string adapterType) => Message.Of("releases.source.type-unavailable", ("type", adapterType));

    /// <summary>
    /// The sentence for a source <c>ReleaseSearchPlanning</c> makes no query for. MAM-32 — the movie and the
    /// TV searches both; a season is searched for in TV alone, so for one it says why TV can't be.
    /// </summary>
    internal static Message NoUsableSearchError(string name, int? season) =>
        season is null
            ? Message.Of("releases.source.no-usable-search", ("source", name))
            : Message.Of("releases.source.no-usable-season-search", ("source", name));

    /// <summary>
    /// Asks every enabled source, concurrently, the query families <paramref name="plan"/> makes from what each can
    /// search by; one outcome per source, in name order. MAM-33 — the machinery the plain, the stored, the kind and
    /// the newest searches share; only what each asks differs.
    /// </summary>
    /// <param name="plan">The families of tiers to send a source, from its capabilities; none at all, or only empty
    /// ones, means the source offers nothing this search can use.</param>
    /// <param name="noUsableSearchError">The sentence for such a source, from its name; <c>null</c> when no reader is shown
    /// one.</param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    private async Task<IReadOnlyList<ReleaseSourceOutcome>> AskEachSourceAsync(
        Func<ReleaseSearchCapabilities, IReadOnlyList<IReadOnlyList<ReleaseSearchQuery>>> plan,
        Func<string, Message?> noUsableSearchError,
        CancellationToken cancellationToken)
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
            integrations.Select(integration => SearchSourceAsync(integration, plan, noUsableSearchError, cancellationToken)));
    }

    /// <summary>
    /// One source, from its row to what it listed. Never throws but for the caller's cancellation: a
    /// source's failure is its own answer (ADR 0004 — "a Toloka failure shows an explicit source-specific
    /// error").
    /// </summary>
    private async Task<ReleaseSourceOutcome> SearchSourceAsync(
        IntegrationConnectionEntity integration,
        Func<ReleaseSearchCapabilities, IReadOnlyList<IReadOnlyList<ReleaseSearchQuery>>> plan,
        Func<string, Message?> noUsableSearchError,
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

            var searchable = capabilities.Value!;
            var families = plan(searchable);
            if (families.All(family => family.Count == 0))
                return NotSearched(integration, ReleaseSourceNotSearchedReason.NoUsableSearch, noUsableSearchError(integration.Name));

            // Each family's tiers in order until one lists a release (ReleaseSearchPlanning): an id search
            // that finds nothing is followed by the title, never reported as "nothing there". The families
            // one after the other, as the tiers are: a source is still sent one request at a time, and lists
            // its releases in the same order on every search. Any query that fails fails the source, as
            // before: half of what it lists would read as all of it.
            var hits = new List<ReleaseSearchHit>();
            var listedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var family in families)
            {
                IReadOnlyList<ReleaseSearchHit> found = [];
                foreach (var query in family)
                {
                    var answer = await provider.SearchAsync(settings, query, cancellationToken);
                    if (!answer.IsSuccess)
                        return Failed(integration, answer.Status, answer.Error!, settings);
                    found = answer.Value!;
                    if (found.Count > 0)
                        break;
                }

                // A listing both families find (filed under a movie and a TV category both) is kept once, as
                // the movie family found it: two rows of one release would list it twice. One the source
                // gives no id is never the same as another.
                hits.AddRange(found.Where(hit => hit.SourceItemId is null || !listedIds.Contains(hit.SourceItemId)));
                listedIds.UnionWith(found.Select(hit => hit.SourceItemId).OfType<string>());
            }

            logger.LogInformation(
                "Release search of integration {IntegrationId} ({AdapterType}) found {Count} releases.",
                integration.Id,
                integration.AdapterType,
                hits.Count);
            return new ReleaseSourceOutcome(
                integration.Id, integration.Name, IntegrationCallStatus.Succeeded, null, null, hits, searchable.Categories);
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
                integration, IntegrationCallStatus.UnexpectedResponse, IntegrationAdapterMessages.UnreadableAnswer(integration.Name), settings);
        }
    }

    private ReleaseSourceOutcome NotSearched(IntegrationConnectionEntity integration, ReleaseSourceNotSearchedReason reason, Message? error)
    {
        logger.LogInformation(
            "Release search of integration {IntegrationId} ({AdapterType}) did not ask it: {Reason}.",
            integration.Id,
            integration.AdapterType,
            reason);
        return new ReleaseSourceOutcome(integration.Id, integration.Name, null, reason, error, [], []);
    }

    private ReleaseSourceOutcome Failed(
        IntegrationConnectionEntity integration, IntegrationCallStatus status, Message error, IntegrationConnectionSettings settings)
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
            integration.Id, integration.Name, status, null, error.Redacted(settings.Secrets), [], []);
    }
}
