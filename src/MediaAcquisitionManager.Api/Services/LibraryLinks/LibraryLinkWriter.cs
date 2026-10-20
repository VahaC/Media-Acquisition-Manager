using System.Globalization;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkWriter"/>
/// <remarks>
/// A bug that throws out of an identification settles the link <c>NotWritten</c> with <see cref="AppErrorMessage"/> and is
/// logged as an Error, instead of leaving it <c>Writing</c>, which would refuse every change of the work until the next
/// restart.
/// </remarks>
public sealed class LibraryLinkWriter(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    ICatalogConnector catalogs,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<LibraryLinkWriter> logger) : ILibraryLinkWriter
{
    internal static readonly Message AppErrorMessage = Message.Of("library-links.write.app-error");

    /// <summary>
    /// How long after the audit row of a series' last Apply the app keeps from sending another for it. Jellyfin's Apply runs
    /// on after the client that sent it gave up (its server side is never cancelled) and keeps rewriting seasons and
    /// episodes for minutes, so an immediate Retry or repair click would read a half-refreshed series — which can pass the
    /// confirmation early — or send a second Apply that deletes the episode images again. Ten minutes is the longest
    /// series the owner's library holds (Penguins of Madagascar, 149 episodes, 159 s live) with margin; a film's Apply takes
    /// seconds and needs no such wait.
    /// </summary>
    private static readonly TimeSpan SeriesWait = TimeSpan.FromMinutes(10);

    /// <summary>R12 — a library switched off is never identified in; the link waits for it.</summary>
    internal static Message SwitchedOffError(string name) => Message.Of("library-links.write.switched-off", ("integration", name));

    /// <summary>The series wait of <see cref="SeriesWait"/>: when the last attempt's ten minutes are over, in UTC.</summary>
    /// <remarks>MAM-47 (ADR 0042) — the one time of day a stored message holds: kept as the text <c>HH:mm</c> it always was
    /// (the template owns "UTC"), as the reader reads it the same in any language.</remarks>
    internal static Message SeriesStillWorkingError(DateTime untilUtc) =>
        Message.Of("library-links.write.series-still-working", ("time", untilUtc.ToString("HH:mm", CultureInfo.InvariantCulture)));

    public async Task<LibraryLinkWriteResult> WriteAsync(Guid linkId, Guid attemptId, AuditActor? actor, CancellationToken cancellationToken = default)
    {
        if (await ClaimedAsync(linkId, attemptId, cancellationToken) is not { } link)
            return LibraryLinkWriteResult.ClaimLost;

        try
        {
            return await IdentifyClaimedAsync(link, attemptId, actor, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Identifying TMDb link {LinkId} in its library threw.", link.Id);
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, AppErrorMessage);
        }
    }

    // ── identifying ──────────────────────────────────────────────────────────────────────────────

    private async Task<LibraryLinkWriteResult> IdentifyClaimedAsync(LibraryItemLinkEntity link, Guid attemptId, AuditActor? actor, CancellationToken cancellationToken)
    {
        // Removed with its links (decision 12): nothing is left to settle.
        if (await IntegrationOfAsync(link, cancellationToken) is not { } integration)
            return LibraryLinkWriteResult.ClaimLost;

        // Only an item of a kind its library identifies is ever sent (Jellyfin: a film or a series, never an episode item).
        if (adapters.Find<IMediaLibraryItemIdentifier>(integration.AdapterType) is not { } identifier
            || LibraryLinkWritability.ItemReason(identifier, integration.Name, link.ItemKind) is not null)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, null);

        // A link with no id of its own to identify by — an episode's without its numbers — stays in the app: the library
        // has nothing to be asked for.
        if (LibraryLinkWritability.LinkReason(link.Target, link.SeasonNumber, link.EpisodeNumber) is not null)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, null);
        if (!integration.IsEnabled)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, SwitchedOffError(integration.Name));

        // MAM-34 — an episode's link is identified by the episode's own IMDb id, asked of TMDb when the link doesn't hold it
        // yet: a link set before the app identified episodes (the repair), or one whose TMDb couldn't be asked then. TMDb's
        // "none" that the save claiming the link just heard is on the link already, and isn't asked again.
        if (link is { Target: LibraryLinkTarget.ShowEpisode, EpisodeImdbId: null })
        {
            var episode = LibraryLinkWritability.KnowsNoEpisodeImdb(link)
                ? new LibraryLinkEpisodeImdb(null, null)
                : await EpisodeImdbAsync(link, cancellationToken);
            if (episode.Error is { } catalogError)
                return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, catalogError);
            if (episode.ImdbId is not { } episodeImdbId)
                return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, LibraryLinkWritability.NoEpisodeImdbReason);
            if (!await KeepEpisodeImdbIdAsync(link, attemptId, episodeImdbId))
                return LibraryLinkWriteResult.ClaimLost;
            link.EpisodeImdbId = episodeImdbId;
        }

        if (await SeriesWaitAsync(link, cancellationToken) is { } waitsUntilUtc)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, SeriesStillWorkingError(waitsUntilUtc));

        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, built.Error!);

        // The version gate (MAM-8), as before every sync: a server the app can't identify in safely is never sent anything.
        var adapter = adapters.Find(integration.AdapterType)!;
        var test = await adapter.TestConnectionAsync(settings, cancellationToken);
        if (!test.IsSuccess)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, Masked(test.Message, settings));

        var request = new LibraryIdentifyRequest(
            link.SourceItemId,
            link.ItemKind,
            link.Target == LibraryLinkTarget.Work
                ? new MediaIdentifier(link.Scheme, link.Value)
                : new MediaIdentifier(MediaIdentifierScheme.Imdb, link.EpisodeImdbId!),
            // ADR 0026 — a bulk click (Accept all, the repair) never replaces an id the library holds on its own; the owner's
            // own Save, Replace and Retry in the dialog is his word and does (Retry clears the batch for that reason).
            ProtectHeldIds: link.BatchId is not null);
        // Never cancelled once claimed (ADR 0013): the identification runs to its end, and is never sent twice.
        var result = await identifier.IdentifyAsync(settings, request, CancellationToken.None);
        if (!result.IsSuccess)
        {
            // Warning, so it ships off the box: the owner's library refused or couldn't be reached. The status only. Nothing
            // was sent, so the item holds what it held: no audit row, and its identified time is kept.
            logger.LogWarning("TMDb link {LinkId} was not identified in its library: {Status}.", link.Id, result.Status);
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, Masked(result.Error!, settings));
        }

        var identification = result.Value!;
        if (identification.Sent)
        {
            // R10 — whatever left the app is recorded, confirmed or not: it is the app's only record that an Apply was sent,
            // read by the repair's selection and the series wait. Saved before the link settles, so a settle that fails still
            // leaves the record.
            auditTrail.Record(
                AuditAction.LibraryItemIdentified,
                actor,
                AuditTarget.LibraryLink(link.Id, TargetName(link)),
                LibraryLinkKeys.AuditIdentifiers(identification.IdsBefore, identification.IdsAfter));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        logger.LogInformation(
            "TMDb link {LinkId} was identified in its library: {Outcome}, sent {Sent}.", link.Id, identification.Outcome, identification.Sent);
        var sentence = Masked(identification.Sentence ?? AppErrorMessage, settings);
        switch (identification.Outcome)
        {
            case LibraryIdentifyOutcome.Identified or LibraryIdentifyOutcome.AlreadyIdentified:
                return await SettleAsync(link, attemptId, LibraryLinkWriteState.Written, null, IdentifiedChange.Set, identification.Outcome);
            case LibraryIdentifyOutcome.NotConfirmed:
                // An Apply may have damaged the item (it deletes the images first): the repair must see the link again.
                return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, sentence, IdentifiedChange.Clear, identification.Outcome);
            default:
                if (identification.Refusal is { } refusal)
                    logger.LogWarning("TMDb link {LinkId} was refused by its library's identification: {Refusal}.", link.Id, refusal);
                // A refusal or a missing match changed nothing: the identified time is kept.
                return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, sentence, IdentifiedChange.Keep, identification.Outcome);
        }
    }

    /// <summary>
    /// When the series wait of <see cref="SeriesWait"/> is over — in UTC — for a series link not identified yet whose last
    /// Apply left the app less than that ago, read from its <c>LibraryItemIdentified</c> audit row; <c>null</c> when there is
    /// nothing to wait for (a film, an identified series, no Apply sent since the link was set, or the wait is over).
    /// </summary>
    private async Task<DateTime?> SeriesWaitAsync(LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        if (link.ItemKind != MediaWorkKind.Series || link.IdentifiedUtc is not null)
            return null;

        var lastAttemptUtc = await db.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.Action == AuditAction.LibraryItemIdentified
                && auditEvent.TargetKind == AuditTargetKind.LibraryLink
                && auditEvent.TargetId == link.Id
                && auditEvent.CreatedUtc >= link.SetUtc)
            .OrderByDescending(auditEvent => auditEvent.CreatedUtc)
            .Select(auditEvent => (DateTime?)auditEvent.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return lastAttemptUtc is { } attemptUtc && time.GetUtcNow().UtcDateTime < attemptUtc + SeriesWait ? attemptUtc + SeriesWait : null;
    }

    /// <summary>MAM-34 — the episode's IMDb id from the metadata catalog, or why it couldn't be asked.</summary>
    private async Task<LibraryLinkEpisodeImdb> EpisodeImdbAsync(LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        var connected = await catalogs.ConnectAsync(cancellationToken);
        if (connected.Connection is not { } catalog)
            return new LibraryLinkEpisodeImdb(null, connected.Error!);

        var episode = await LibraryLinkEpisodeImdb.AskAsync(catalog, link.Value, link.SeasonNumber!.Value, link.EpisodeNumber!.Value, cancellationToken);
        if (episode.Error is not null)
            logger.LogWarning("TMDb couldn't be asked for the IMDb id of the episode TMDb link {LinkId} names.", link.Id);
        return episode;
    }

    /// <summary>Keeps the episode's IMDb id TMDb gave on the claimed link, before anything is sent: a restart that cuts the
    /// identification finds it there. <c>false</c> when the claim was lost meanwhile.</summary>
    private async Task<bool> KeepEpisodeImdbIdAsync(LibraryItemLinkEntity link, Guid attemptId, string episodeImdbId) =>
        await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EpisodeImdbId, episodeImdbId), CancellationToken.None) == 1;

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private Task<LibraryItemLinkEntity?> ClaimedAsync(Guid linkId, Guid attemptId, CancellationToken cancellationToken) =>
        db.LibraryItemLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                link => link.Id == linkId && link.WriteState == LibraryLinkWriteState.Writing && link.WriteAttemptId == attemptId,
                cancellationToken);

    private Task<IntegrationConnectionEntity?> IntegrationOfAsync(LibraryItemLinkEntity link, CancellationToken cancellationToken) =>
        db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(integration => integration.Id == link.IntegrationConnectionId, cancellationToken);

    /// <summary>What a settle does to <see cref="LibraryItemLinkEntity.IdentifiedUtc"/>.</summary>
    private enum IdentifiedChange
    {
        /// <summary>Leaves it as it is: nothing happened to the item.</summary>
        Keep,

        /// <summary>Sets it to now: the item shows the link's match.</summary>
        Set,

        /// <summary>Clears it: an Apply was sent and the item doesn't show the match.</summary>
        Clear,
    }

    /// <summary>
    /// The claimed link's final state (R9): only while it is still <c>Writing</c> under this claim, never cancelled —
    /// once the library was called, its outcome is recorded whatever happened to the caller. MAM-47 (ADR 0042) — the error
    /// is stored as key and values, fitted into its column by the column's converter, so nothing is cut here.
    /// </summary>
    private async Task<LibraryLinkWriteResult> SettleAsync(
        LibraryItemLinkEntity link,
        Guid attemptId,
        LibraryLinkWriteState state,
        Message? error,
        IdentifiedChange identified = IdentifiedChange.Keep,
        LibraryIdentifyOutcome? outcome = null)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var settled = await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters
                        .SetProperty(row => row.WriteState, state)
                        .SetProperty(row => row.WriteError, error)
                        .SetProperty(row => row.WriteStateUtc, now);
                    if (identified == IdentifiedChange.Set)
                        setters.SetProperty(row => row.IdentifiedUtc, (DateTime?)now);
                    else if (identified == IdentifiedChange.Clear)
                        setters.SetProperty(row => row.IdentifiedUtc, (DateTime?)null);
                },
                CancellationToken.None) == 1;
        if (!settled)
            return LibraryLinkWriteResult.ClaimLost;

        return new LibraryLinkWriteResult(
            state switch
            {
                LibraryLinkWriteState.Written => LibraryLinkWriteStatus.Written,
                LibraryLinkWriteState.KeptInApp => LibraryLinkWriteStatus.KeptInApp,
                _ => LibraryLinkWriteStatus.NotWritten,
            },
            error,
            outcome);
    }

    // The messages already come masked from the adapter; their values are masked again here (ADR 0007's "masked twice").
    private static Message Masked(Message error, IntegrationConnectionSettings settings) => error.Redacted(settings.Secrets);

    /// <summary>The audit row's name of the link's identifications: the item's title when it was linked, else the
    /// catalog's, else the item's id.</summary>
    internal static string TargetName(LibraryItemLinkEntity link) => link.ItemTitle ?? link.CatalogTitle ?? link.SourceItemId;
}
