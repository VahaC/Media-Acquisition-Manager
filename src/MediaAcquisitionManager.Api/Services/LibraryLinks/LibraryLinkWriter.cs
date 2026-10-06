using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkWriter"/>
/// <remarks>
/// A bug that throws out of a write — the adapter refusing a change the rules built — settles the link
/// <c>NotWritten</c> with <see cref="AppErrorMessage"/> and is logged as an Error, instead of leaving it <c>Writing</c>,
/// which would refuse every change of the work until the next restart.
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
    internal const string AppErrorMessage = "The write stopped because of an error in the app. Its log says more.";

    /// <summary>R12 — a library switched off is never written; the link waits for it.</summary>
    internal static string SwitchedOffError(string name) => $"“{name}” is switched off. Switch it on under Integrations, then retry.";

    /// <summary>R11 — a library switched off can't be changed back, so the link it holds stays.</summary>
    internal static string SwitchedOffToRemoveError(string name) =>
        $"“{name}” is switched off. Switch it on under Integrations to remove the link.";

    /// <summary>R10 — sent, but the read afterwards didn't show it, and the library gave no reason of its own.</summary>
    internal static string NotConfirmedError(string name) => $"“{name}” took the change but still reports other ids. Retry.";

    /// <summary>What the app wrote for a link replaced by one that isn't written couldn't be put back.</summary>
    internal static string NotRestoredError(string name, string error) => $"“{name}” couldn't be changed back: {error}";

    /// <summary>
    /// MAM-34 — an episode's IMDb id goes only where the item holds none, and the item holds another: the app never writes
    /// over the library's own ids, so the owner changes it in the library (then Retry, or the next sync, finds it).
    /// </summary>
    internal static string HoldsAnotherImdbError(string name, string episodeImdbId) =>
        $"“{name}” holds another IMDb id for this item, and the app never writes over the library's own ids, so the episode's {episodeImdbId} isn't in it. Change it there, then retry.";

    public async Task<LibraryLinkWriteResult> WriteAsync(Guid linkId, Guid attemptId, AuditActor? actor, CancellationToken cancellationToken = default)
    {
        if (await ClaimedAsync(linkId, attemptId, cancellationToken) is not { } link)
            return LibraryLinkWriteResult.ClaimLost;

        try
        {
            return await WriteClaimedAsync(link, attemptId, actor, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Writing TMDb link {LinkId} into its library threw.", link.Id);
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, AppErrorMessage);
        }
    }

    public async Task<LibraryLinkRestoreResult> RestoreAsync(Guid linkId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        // A link no longer this caller's: the removal that follows is conditional on the claim and refuses.
        if (await ClaimedAsync(linkId, attemptId, cancellationToken) is not { } link)
            return LibraryLinkRestoreResult.NotNeeded;

        try
        {
            return await RestoreClaimedAsync(link, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Restoring the library item of TMDb link {LinkId} threw.", link.Id);
            return LibraryLinkRestoreResult.Failed(AppErrorMessage);
        }
    }

    // ── writing ──────────────────────────────────────────────────────────────────────────────────

    private async Task<LibraryLinkWriteResult> WriteClaimedAsync(LibraryItemLinkEntity link, Guid attemptId, AuditActor? actor, CancellationToken cancellationToken)
    {
        // Removed with its links (decision 12): nothing is left to settle.
        if (await IntegrationOfAsync(link, cancellationToken) is not { } integration)
            return LibraryLinkWriteResult.ClaimLost;

        // Only an item of a kind its library writes is ever sent (Jellyfin: a film or a series, never an episode item); a
        // link on any other stays in the app, and nothing of it was ever written to put back.
        if (adapters.Find<IMediaLibraryIdentifierWriter>(integration.AdapterType) is not { } writer
            || LibraryLinkWritability.ItemReason(writer, integration.Name, link.ItemKind) is not null)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, null);

        var history = await LibraryLinkWriteHistory.ReadAsync(db, link, cancellationToken);
        // A link that writes no ids of its own — an episode's link without its numbers — is kept in the app, after what
        // the app wrote for the item before is put back (a film link replaced by it, R11).
        if (LibraryLinkWritability.LinkReason(link.Target, link.SeasonNumber, link.EpisodeNumber) is not null)
            return await RestoreAndKeepAsync(link, attemptId, integration, writer, history, null, actor, cancellationToken);
        if (!integration.IsEnabled)
            return await SentNothingAsync(link, attemptId, SwitchedOffError(integration.Name));

        // MAM-34 — an episode's link writes the episode's own IMDb id, asked of TMDb when the link doesn't hold it yet: a
        // link set before the app wrote episodes (the catch-up), or one whose TMDb couldn't be asked then. TMDb's "none"
        // that the save claiming the link just heard is on the link already, and isn't asked again.
        if (link is { Target: LibraryLinkTarget.ShowEpisode, EpisodeImdbId: null })
        {
            var episode = LibraryLinkWritability.KnowsNoEpisodeImdb(link)
                ? new LibraryLinkEpisodeImdb(null, null)
                : await EpisodeImdbAsync(link, cancellationToken);
            if (episode.Error is { } catalogError)
                return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, catalogError);
            if (episode.ImdbId is not { } episodeImdbId)
                return await RestoreAndKeepAsync(link, attemptId, integration, writer, history, LibraryLinkWritability.NoEpisodeImdbReason, actor, cancellationToken);
            if (!await KeepEpisodeImdbIdAsync(link, attemptId, episodeImdbId))
                return LibraryLinkWriteResult.ClaimLost;
            link.EpisodeImdbId = episodeImdbId;
        }

        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return await SentNothingAsync(link, attemptId, built.Error!);

        // The version gate (MAM-8), as before every sync: a server the app can't write to safely is never sent a change.
        var adapter = adapters.Find(integration.AdapterType)!;
        var test = await adapter.TestConnectionAsync(settings, cancellationToken);
        if (!test.IsSuccess)
            return await SentNothingAsync(link, attemptId, Masked(test.Message, settings));

        var edits = LibraryLinkEdits.Write(link, history);
        var result = await writer.WriteIdentifiersAsync(settings, new LibraryIdentifierChange(link.SourceItemId, link.ItemKind, edits), cancellationToken);
        if (!result.IsSuccess)
        {
            // Warning, so it ships off the box: the owner's library refused or couldn't be reached. The status only.
            logger.LogWarning("TMDb link {LinkId} was not written into its library: {Status}.", link.Id, result.Status);
            // A refused write changed nothing (IMediaLibraryIdentifierWriter): the item holds what it held.
            return await SentNothingAsync(link, attemptId, Masked(result.Error!, settings));
        }

        var write = result.Value!;
        if (write.Sent)
        {
            // R10 — whatever left the app is recorded, confirmed or not: a later removal starts from this row.
            auditTrail.Record(
                AuditAction.LibraryIdsWritten,
                actor,
                AuditTarget.LibraryLink(link.Id, TargetName(link)),
                LibraryLinkEdits.AuditIdentifiers(write.Before, LibraryLinkEdits.After(write, edits)));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        logger.LogInformation("TMDb link {LinkId} was written into its library: sent {Sent}, confirmed {Confirmed}.", link.Id, write.Sent, write.Confirmed);
        if (!write.Confirmed)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, Masked(write.Problem ?? NotConfirmedError(integration.Name), settings));
        // An IMDb id goes only where the item holds none: an episode's whose item holds another isn't in the library,
        // however confirmed the write (it left that id alone), and the owner is told why.
        if (LibraryLinkEdits.WrittenKey(link) is { } key && !LibraryLinkEdits.Holds(write.Sent ? write.After : write.Before, key))
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, HoldsAnotherImdbError(integration.Name, key.Value));

        var written = await SettleAsync(link, attemptId, LibraryLinkWriteState.Written, null);
        if (written.Status == LibraryLinkWriteStatus.Written)
            await RequestLookupAsync(link, attemptId, writer, settings, actor);
        return written;
    }

    /// <summary>
    /// What the app wrote for the item before put back (R11) — a film link replaced by a link that writes nothing of its
    /// own — and the link settled <c>KeptInApp</c> with <paramref name="keptError"/>, or with the sentence why the item
    /// couldn't be put back. Nothing to put back sends nothing.
    /// </summary>
    private async Task<LibraryLinkWriteResult> RestoreAndKeepAsync(
        LibraryItemLinkEntity link,
        Guid attemptId,
        IntegrationConnectionEntity integration,
        IMediaLibraryIdentifierWriter writer,
        LibraryLinkWriteHistory? history,
        string? keptError,
        AuditActor? actor,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LibraryIdentifierEdit> edits = history is null ? [] : LibraryLinkEdits.Restore(history);
        if (edits.Count == 0)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, keptError);
        if (!integration.IsEnabled)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, NotRestoredError(integration.Name, SwitchedOffError(integration.Name)));

        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, NotRestoredError(integration.Name, built.Error!));
        var test = await adapters.Find(integration.AdapterType)!.TestConnectionAsync(settings, cancellationToken);
        if (!test.IsSuccess)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, NotRestoredError(integration.Name, Masked(test.Message, settings)));

        var result = await writer.WriteIdentifiersAsync(settings, new LibraryIdentifierChange(link.SourceItemId, link.ItemKind, edits), cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogWarning("The library item of TMDb link {LinkId} was not put back: {Status}.", link.Id, result.Status);
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, NotRestoredError(integration.Name, Masked(result.Error!, settings)));
        }

        var write = result.Value!;
        if (write.Sent)
        {
            auditTrail.Record(
                AuditAction.LibraryIdsRestored,
                actor,
                AuditTarget.LibraryLink(link.Id, TargetName(link)),
                LibraryLinkEdits.AuditIdentifiers(write.Before, LibraryLinkEdits.After(write, edits)));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return await SettleAsync(
            link,
            attemptId,
            LibraryLinkWriteState.KeptInApp,
            write.Confirmed ? keptError : NotRestoredError(integration.Name, Masked(write.Problem ?? NotConfirmedError(integration.Name), settings)));
    }

    /// <summary>
    /// MAM-34 — asks the library to look up the item's missing metadata now that its ids are in (Vasyl's decision of
    /// 2026-10-06), and records it: when, and why the library didn't take it, on the link; an audit row of the request.
    /// Best effort: whatever comes of it, the link stays <c>Written</c> — the ids are in —, and Retry asks again.
    /// </summary>
    /// <remarks>
    /// After the link is settled, not under its claim: a slow lookup must not keep a written link "being written". Its
    /// record goes only onto the link this write settled (the same attempt), so a later write's record stands.
    /// </remarks>
    private async Task RequestLookupAsync(
        LibraryItemLinkEntity link, Guid attemptId, IMediaLibraryIdentifierWriter writer, IntegrationConnectionSettings settings, AuditActor? actor)
    {
        try
        {
            // Never cancelled: the write it follows ran to its end whether or not the browser was still there (ADR 0013).
            var lookup = await writer.RequestMetadataLookupAsync(settings, link.SourceItemId, link.ItemKind, CancellationToken.None);
            string? error = null;
            if (!lookup.IsSuccess)
            {
                logger.LogWarning("The library didn't take the metadata lookup of TMDb link {LinkId}: {Status}.", link.Id, lookup.Status);
                error = FileInspectionRunUpdates.Cut(Masked(lookup.Error!, settings), LibraryItemLinkEntity.ErrorMaximumLength);
            }

            auditTrail.Record(AuditAction.LibraryMetadataLookupRequested, actor, AuditTarget.LibraryLink(link.Id, TargetName(link)));
            await db.SaveChangesAsync(CancellationToken.None);
            await db.LibraryItemLinks
                .Where(row => row.Id == link.Id && row.WriteAttemptId == attemptId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.LookupRequestedUtc, time.GetUtcNow().UtcDateTime)
                        .SetProperty(row => row.LookupError, error),
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A bug, or a database that failed: logged, never the write's failure — the ids are in the library.
            logger.LogError(exception, "Asking the library to look up the item of TMDb link {LinkId} threw.", link.Id);
        }
    }

    /// <summary>
    /// A write that sent nothing — the library switched off, its secrets unreadable, unreachable or refusing — settled:
    /// <c>NotWritten</c> with <paramref name="error"/>, unless the last sync read the link's written key in its item. Then
    /// the ids are in the library as far as the app knows — the sync's own judgment (R5) —, and only the lookup the write
    /// was to ask for is missed: the link is settled <c>InLibrary</c> with the sentence as its lookup's error (MAM-34). A
    /// Retry or a catch-up that only meant to ask for the lookup so never turns a link the library holds into "Not in
    /// Jellyfin yet", and the catch-up counts its lookup again.
    /// </summary>
    private async Task<LibraryLinkWriteResult> SentNothingAsync(LibraryItemLinkEntity link, Guid attemptId, string error)
    {
        if (LibraryLinkEdits.WrittenKey(link) is not { } key || !await LastSyncHoldsAsync(link, key))
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, error);

        var storedError = FileInspectionRunUpdates.Cut(error, LibraryItemLinkEntity.ErrorMaximumLength);
        var settled = await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.WriteState, LibraryLinkWriteState.InLibrary)
                    .SetProperty(row => row.WriteError, (string?)null)
                    .SetProperty(row => row.WriteStateUtc, time.GetUtcNow().UtcDateTime)
                    .SetProperty(row => row.LookupError, storedError),
                CancellationToken.None) == 1;
        // Not written by this write, whatever the library holds: what the background writer counts it as.
        return settled ? new LibraryLinkWriteResult(LibraryLinkWriteStatus.NotWritten, storedError) : LibraryLinkWriteResult.ClaimLost;
    }

    /// <summary>Whether the last sync of the link's library read <paramref name="key"/> in its item — the library's rows
    /// of the snapshot, as a sync judges a link by them (R5).</summary>
    private async Task<bool> LastSyncHoldsAsync(LibraryItemLinkEntity link, MediaIdentifier key)
    {
        // Settling, never cancelled (R9): the claim is this write's to end.
        var values = await db.MediaWorkIdentifiers
            .AsNoTracking()
            .Where(row => row.SourceKind == ProvenanceSourceKind.MediaLibrary
                && row.SourceConnectionId == link.IntegrationConnectionId
                && row.SourceItemId == link.SourceItemId
                && row.Scheme == key.Scheme)
            .Select(row => row.Value)
            .ToListAsync(CancellationToken.None);
        return values.Any(value => LibraryLinkEdits.Holds([new MediaIdentifier(key.Scheme, value)], key));
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

    /// <summary>Keeps the episode's IMDb id TMDb gave on the claimed link, before anything is sent: a write a restart cuts
    /// is then recorded with it (R10). <c>false</c> when the claim was lost meanwhile.</summary>
    private async Task<bool> KeepEpisodeImdbIdAsync(LibraryItemLinkEntity link, Guid attemptId, string episodeImdbId) =>
        await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EpisodeImdbId, episodeImdbId), CancellationToken.None) == 1;

    // ── restoring ────────────────────────────────────────────────────────────────────────────────

    private async Task<LibraryLinkRestoreResult> RestoreClaimedAsync(LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        // R11 — an unattached link's item is gone from the library's list: there is no item to put back. Nor is there
        // for a link on a kind of item its library doesn't write (an episode in Jellyfin): it was never written.
        if (link.MediaWorkId is null)
            return LibraryLinkRestoreResult.NotNeeded;
        if (await IntegrationOfAsync(link, cancellationToken) is not { } integration
            || adapters.Find<IMediaLibraryIdentifierWriter>(integration.AdapterType) is not { } writer
            || !writer.WritableKinds.Contains(link.ItemKind))
            return LibraryLinkRestoreResult.NotNeeded;
        if (await LibraryLinkWriteHistory.ReadAsync(db, link, cancellationToken) is not { } history
            || LibraryLinkEdits.Restore(history) is not { Count: > 0 } edits)
            return LibraryLinkRestoreResult.NotNeeded;

        if (!integration.IsEnabled)
            return LibraryLinkRestoreResult.SwitchedOff(SwitchedOffToRemoveError(integration.Name));
        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return LibraryLinkRestoreResult.Failed(built.Error!);
        var test = await adapters.Find(integration.AdapterType)!.TestConnectionAsync(settings, cancellationToken);
        if (!test.IsSuccess)
            return LibraryLinkRestoreResult.Failed(Masked(test.Message, settings));

        var result = await writer.WriteIdentifiersAsync(settings, new LibraryIdentifierChange(link.SourceItemId, link.ItemKind, edits), cancellationToken);
        logger.LogInformation("The library item of TMDb link {LinkId} was put back: {Status}, sent {Sent}.", link.Id, result.Status, result.Value?.Sent ?? false);
        return result switch
        {
            { Status: IntegrationCallStatus.NotFound } => LibraryLinkRestoreResult.ItemGone,
            { IsSuccess: false } => LibraryLinkRestoreResult.Failed(Masked(result.Error!, settings)),
            { Value.Sent: false } => LibraryLinkRestoreResult.NotNeeded,
            _ => LibraryLinkRestoreResult.Restored(result.Value! with { After = LibraryLinkEdits.After(result.Value!, edits) }),
        };
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private Task<LibraryItemLinkEntity?> ClaimedAsync(Guid linkId, Guid attemptId, CancellationToken cancellationToken) =>
        db.LibraryItemLinks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                link => link.Id == linkId && link.WriteState == LibraryLinkWriteState.Writing && link.WriteAttemptId == attemptId,
                cancellationToken);

    private Task<IntegrationConnectionEntity?> IntegrationOfAsync(LibraryItemLinkEntity link, CancellationToken cancellationToken) =>
        db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(integration => integration.Id == link.IntegrationConnectionId, cancellationToken);

    /// <summary>
    /// The claimed link's final state (R9): only while it is still <c>Writing</c> under this claim, never cancelled —
    /// once the library was called, its outcome is recorded whatever happened to the caller.
    /// </summary>
    private async Task<LibraryLinkWriteResult> SettleAsync(LibraryItemLinkEntity link, Guid attemptId, LibraryLinkWriteState state, string? error)
    {
        var storedError = error is null ? null : FileInspectionRunUpdates.Cut(error, LibraryItemLinkEntity.ErrorMaximumLength);
        var settled = await db.LibraryItemLinks
            .Where(row => row.Id == link.Id && row.WriteState == LibraryLinkWriteState.Writing && row.WriteAttemptId == attemptId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.WriteState, state)
                    .SetProperty(row => row.WriteError, storedError)
                    .SetProperty(row => row.WriteStateUtc, time.GetUtcNow().UtcDateTime),
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
            storedError);
    }

    // The sentences already come masked from the adapter; masked again here (ADR 0007's "masked twice").
    private static string Masked(string error, IntegrationConnectionSettings settings) => SecretRedaction.Redact(error, settings.Secrets)!;

    /// <summary>The audit row's name of the link's writes: the item's title when it was linked, else the catalog's, else
    /// the item's id.</summary>
    internal static string TargetName(LibraryItemLinkEntity link) => link.ItemTitle ?? link.CatalogTitle ?? link.SourceItemId;
}
