using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Library;
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

        // A link to the work itself is written; any other is never written, but what the app wrote for the link before —
        // a film link replaced by an episode's link to its show — is put back first (R11).
        var toWork = link.Target == LibraryLinkTarget.Work;
        var unwritten = toWork ? LibraryLinkWriteState.NotWritten : LibraryLinkWriteState.KeptInApp;
        // Only an item of a kind its library writes is ever sent (Jellyfin: a film — never a series, whose save rewrites
        // all its seasons and episodes); any other link stays in the app, and nothing of it was ever written to put back.
        var writer = adapters.Find<IMediaLibraryIdentifierWriter>(integration.AdapterType);
        if (writer is null || !writer.WritableKinds.Contains(link.ItemKind))
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, null);

        var history = await LibraryLinkWriteHistory.ReadAsync(db, link, cancellationToken);
        IReadOnlyList<LibraryIdentifierEdit> edits = toWork
            ? LibraryLinkEdits.Write(link, history)
            : history is null ? [] : LibraryLinkEdits.Restore(history);
        if (edits.Count == 0)
            return await SettleAsync(link, attemptId, LibraryLinkWriteState.KeptInApp, null);

        if (!integration.IsEnabled)
            return await SettleAsync(link, attemptId, unwritten, Unwritten(toWork, integration, SwitchedOffError(integration.Name)));

        var built = settingsFactory.Create(integration);
        if (built.Settings is not { } settings)
            return await SettleAsync(link, attemptId, unwritten, Unwritten(toWork, integration, built.Error!));

        // The version gate (MAM-8), as before every sync: a server the app can't write to safely is never sent a change.
        var adapter = adapters.Find(integration.AdapterType)!;
        var test = await adapter.TestConnectionAsync(settings, cancellationToken);
        if (!test.IsSuccess)
            return await SettleAsync(link, attemptId, unwritten, Unwritten(toWork, integration, Masked(test.Message, settings)));

        var result = await writer.WriteIdentifiersAsync(settings, new LibraryIdentifierChange(link.SourceItemId, link.ItemKind, edits), cancellationToken);
        if (!result.IsSuccess)
        {
            // Warning, so it ships off the box: the owner's library refused or couldn't be reached. The status only.
            logger.LogWarning("TMDb link {LinkId} was not written into its library: {Status}.", link.Id, result.Status);
            return await SettleAsync(link, attemptId, unwritten, Unwritten(toWork, integration, Masked(result.Error!, settings)));
        }

        var write = result.Value!;
        if (write.Sent)
        {
            // R10 — whatever left the app is recorded, confirmed or not: a later removal starts from this row.
            auditTrail.Record(
                toWork ? AuditAction.LibraryIdsWritten : AuditAction.LibraryIdsRestored,
                actor,
                AuditTarget.LibraryLink(link.Id, TargetName(link)),
                LibraryLinkEdits.AuditIdentifiers(write.Before, LibraryLinkEdits.After(write, edits)));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        if (!toWork)
        {
            return await SettleAsync(
                link, attemptId, LibraryLinkWriteState.KeptInApp,
                write.Confirmed ? null : NotRestoredError(integration.Name, Masked(write.Problem ?? NotConfirmedError(integration.Name), settings)));
        }

        logger.LogInformation("TMDb link {LinkId} was written into its library: sent {Sent}, confirmed {Confirmed}.", link.Id, write.Sent, write.Confirmed);
        return write.Confirmed
            ? await SettleAsync(link, attemptId, LibraryLinkWriteState.Written, null)
            : await SettleAsync(link, attemptId, LibraryLinkWriteState.NotWritten, Masked(write.Problem ?? NotConfirmedError(integration.Name), settings));
    }

    // ── restoring ────────────────────────────────────────────────────────────────────────────────

    private async Task<LibraryLinkRestoreResult> RestoreClaimedAsync(LibraryItemLinkEntity link, CancellationToken cancellationToken)
    {
        // R11 — an unattached link's item is gone from the library's list: there is no item to put back. Nor is there
        // for a link on a kind of item its library doesn't write (a series in Jellyfin): it was never written.
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

    // What a link that couldn't reach its library says: a link to the work itself waits to be written; another one only
    // had something to put back.
    private static string Unwritten(bool toWork, IntegrationConnectionEntity integration, string error) =>
        toWork ? error : NotRestoredError(integration.Name, error);

    // The sentences already come masked from the adapter; masked again here (ADR 0007's "masked twice").
    private static string Masked(string error, IntegrationConnectionSettings settings) => SecretRedaction.Redact(error, settings.Secrets)!;

    /// <summary>The audit row's name of the link's writes: the item's title when it was linked, else the catalog's, else
    /// the item's id.</summary>
    internal static string TargetName(LibraryItemLinkEntity link) => link.ItemTitle ?? link.CatalogTitle ?? link.SourceItemId;
}
