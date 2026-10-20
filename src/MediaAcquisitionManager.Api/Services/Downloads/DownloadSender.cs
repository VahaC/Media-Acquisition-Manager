using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Search;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Persistence;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <inheritdoc cref="IDownloadSender"/>
/// <remarks>
/// <para>
/// MAM-13 — in this order, each step stopping the job with its own sentence: the payload fetched (a release
/// whose link fails sends nothing); its kind one the client takes; its info hashes worked out and recorded
/// on the job; the destination's category confirmed with the client (qBittorrent would otherwise create a
/// category it lacks in its default folder — ADR 0010); the client asked whether it already has the torrent
/// (decision 4: such a job fails rather than adopting a download the app didn't add); then the one
/// submission.
/// </para>
/// <para>
/// MAM-38, ADR 0034 — and just before the submission, whether any of the release's files would land on a
/// different file already in the destination's folder (<see cref="IDownloadOverwriteGuard"/>): the client would
/// write the torrent's pieces into it, and nothing sent afterwards could stop it. Such a job fails with the
/// app's sentence naming the file; any other is sent, and told how it was checked. A check that breaks on a bug
/// fails the job too, with nothing sent.
/// </para>
/// <para>
/// MAM-24 — what the release turned out to be is recorded on the job right after the fetch, every send's. A job that
/// carries a replacement of old copies is sent in strict mode (ADR 0032, Vasyl's decision of 2026-10-09): a release
/// that turns out a magnet link, whose files nothing can check before the client has them; a category whose folder
/// isn't inside a library the app knows; and an overwrite check that finds any file at a place, or couldn't look at
/// every place, each fail the whole job with the app's sentence and hand the client nothing. The owner picks Keep both
/// himself: a replacement is never quietly sent as a plain download.
/// </para>
/// <para>
/// MAM-30 — a client whose destinations are folders (ADR 0039) is asked the same way, its folder found by being the same
/// folder (D2) and checked as itself (D3): one it can't see or use any more refuses the send, as Transmission would
/// otherwise create it in its own filesystem. Its torrents carry the destination's name as their label. A torrent that
/// is BitTorrent v2 only, which such a client may not take, is refused before anything is listed or sent (D12). The
/// app's sentences about a folder destination name the destination, never its folder: members read them (D13).
/// </para>
/// <para>
/// A submission is never sent again (house rule 8). When its answer leaves open whether the client took it —
/// no answer in time, the connection lost, a server error, an answer the app can't read — the client's job
/// list is read for the torrent instead: there, it was taken; not there, the job fails with the client's
/// sentence (specification §3.2, step 6).
/// </para>
/// </remarks>
public sealed class DownloadSender(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    IDestinationChecker destinations,
    IDownloadOverwriteGuard overwrites,
    IEncryptionService encryption,
    TimeProvider time,
    ILogger<DownloadSender> logger) : IDownloadSender
{
    /// <summary>
    /// How long to wait before looking a second time for a torrent the client took but didn't list. A
    /// qBittorrent before 5.2 answers <c>Ok.</c> without an id and adds the torrent a moment later, once
    /// libtorrent has loaded it; half a second covers that on a LAN, and a torrent still not listed after it
    /// is recorded as taken, with its info hash, which the poller finds it by (MAM-14).
    /// </summary>
    private static readonly TimeSpan ConfirmationRetryDelay = TimeSpan.FromMilliseconds(500);

    private static readonly Message LocatorUnreadableError = Message.Of("downloads.sender.locator-unreadable");

    /// <summary>MAM-24 — a replacement whose gateway link turned out a magnet once fetched: its files can't be checked
    /// before the client has them (ADR 0032).</summary>
    private static readonly Message ReplacementMagnetError = Message.Of("downloads.sender.replacement-magnet");

    public async Task SendAsync(
        Guid jobId,
        ReleaseEntity release,
        IntegrationConnectionEntity source,
        IntegrationConnectionEntity client,
        string clientDestinationKey,
        string destinationName,
        bool replacement = false)
    {
        var outcome = await RunAsync(jobId, release, source, client, clientDestinationKey, destinationName, replacement);
        await db.DownloadJobs.FinishAsync(
            jobId, outcome.Status, outcome.ClientJobId, outcome.Error, outcome.ErrorOrigin, time.GetUtcNow().UtcDateTime);
        // Information: the status only — never the sentence, which can quote what a service answered.
        logger.LogInformation(
            "Download job {JobId} to integration {IntegrationId} ({AdapterType}) ended {Status}.",
            jobId,
            client.Id,
            client.AdapterType,
            outcome.Status);
    }

    private async Task<SendOutcome> RunAsync(
        Guid jobId,
        ReleaseEntity release,
        IntegrationConnectionEntity source,
        IntegrationConnectionEntity client,
        string key,
        string destinationName,
        bool replacement)
    {
        // The service checked both types before writing the job; the registry doesn't change while the
        // app runs.
        var provider = adapters.Find<IReleaseSearchProvider>(source.AdapterType)!;
        var downloadClient = adapters.Find<IDownloadClient>(client.AdapterType)!;

        // ── the payload ──
        var sourceSettings = settingsFactory.Create(source);
        if (sourceSettings.Settings is not { } fetchSettings)
            return SendOutcome.Failed(sourceSettings.Error!);
        if (!encryption.TryDecrypt(release.PayloadLocatorEncrypted, ReleasePayloadLocatorPurpose.For(release.Id), out var locator))
        {
            // Warning: a sealed link the installation's key can't open is a key problem the owner should
            // see in the log. The release's id only, never the link.
            logger.LogWarning("The download link of release {ReleaseId} could not be decrypted; download job {JobId} sent nothing.", release.Id, jobId);
            return SendOutcome.Failed(LocatorUnreadableError);
        }

        var fetched = await CallAsync(source, "payload fetch", () => provider.FetchPayloadAsync(fetchSettings, new ReleasePayloadLocator(locator), CancellationToken.None));
        if (!fetched.IsSuccess)
            return Failed(source, DownloadJobErrorOrigin.ReleaseSource, "payload fetch", fetched.Status, fetched.Error!, fetchSettings);
        var payload = fetched.Value!;
        // MAM-24 — what a gateway's link turned out to be is known only now: the payload type a replacement keeps.
        await db.DownloadJobs.RecordPayloadKindAsync(jobId, payload.Kind);
        if (replacement && payload.Kind == ReleasePayloadKind.Magnet)
            return SendOutcome.Failed(ReplacementMagnetError);
        if (!downloadClient.Capabilities.PayloadKinds.Contains(payload.Kind))
            return SendOutcome.Failed(TurnedOutError(client, payload.Kind));

        var hashes = TorrentInfoHashReader.Read(payload);
        // MAM-30, D12 — beside the kind: what the release is, not what the client says, so the app's own sentence, which
        // members read too, and nothing listed or asked first.
        if (hashes is { V1: null, V2: not null } && !downloadClient.Capabilities.TakesV2OnlyTorrents)
            return SendOutcome.Failed(Message.Of("downloads.sender.v2-only", ("client", client.Name)));
        if (hashes.IsKnown)
            await db.DownloadJobs.RecordInfoHashesAsync(jobId, hashes);

        // ── the destination ──
        // Through the Destinations' own listing, which leaves a switched-off client alone and masks what
        // it says, as every read of a client's categories does. MAM-30 — a folder client is proposed this destination's
        // folder, so it is checked as itself (D3).
        var listed = await destinations.ListAsync(client, [key], CancellationToken.None);
        if (listed.Status != ClientDestinationsStatus.Listed)
        {
            // The client's own sentence when it was asked and failed; the app's when it wasn't asked at all.
            return listed.Status == ClientDestinationsStatus.CallFailed
                ? SendOutcome.Failed(listed.Error!, DownloadJobErrorOrigin.DownloadClient)
                : SendOutcome.Failed(listed.Error!);
        }

        // MAM-30 — only a category can be missing: the listing always holds a folder client's stored folder — the client's
        // own entry, or one ListAsync adds with why the app can't use it (D3) —, and that one is FolderNotSeen below.
        if (listed.Find(key) is not { } destination)
        {
            return SendOutcome.Failed(Message.Of("downloads.sender.category-missing", ("client", client.Name), ("category", key)));
        }

        var wording = new DestinationWording(downloadClient.Capabilities.DestinationKind, key, destinationName);
        // MAM-30 — a folder the client can't see or use: Transmission would create it in its own filesystem (D5).
        if (destination.Check.Status == DestinationPathStatus.FolderNotSeen)
            return SendOutcome.Failed(FolderUnusableError(client, destinationName));

        // MAM-24 — a replacement lands only inside a library the app knows: the new file must be one the library reads,
        // or nothing could ever verify it (ADR 0032).
        if (replacement && destination.Check.Status != DestinationPathStatus.InLibrary)
            return SendOutcome.Failed(ReplacementOutsideLibrariesError(wording));

        var clientSettingsResult = settingsFactory.Create(client);
        if (clientSettingsResult.Settings is not { } clientSettings)
            return SendOutcome.Failed(clientSettingsResult.Error!);

        // ── what the client already has ──
        if (hashes.IsKnown)
        {
            var held = await FindAsync(client, downloadClient, clientSettings, hashes);
            if (!held.IsSuccess)
                return Failed(client, DownloadJobErrorOrigin.DownloadClient, "job lookup", held.Status, held.Error!, clientSettings);
            if (ClientJobCorrelation.Match(held.Value!, hashes).Status != ClientJobMatchStatus.None)
                return SendOutcome.Failed(Message.Of("downloads.sender.already-has-torrent", ("client", client.Name)));
        }

        // ── what it would write over (MAM-38) ──
        var overwrite = await CheckOverwritesAsync(jobId, payload, destination, destinationName, client, downloadClient, clientSettings, replacement);
        if (overwrite is null)
            return SendOutcome.Failed(OverwriteCheckFailedError(wording));
        if (overwrite.Refusal is { } refusal)
            return SendOutcome.Failed(refusal);
        await db.DownloadJobs.RecordOverwriteCheckAsync(jobId, overwrite.Check!.Value);

        // ── the one submission ──
        var submitted = await CallAsync(
            client,
            "submission",
            // MAM-30 — the destination's name as the label a folder client shows the torrent with; a category client
            // ignores it.
            () => downloadClient.SubmitAsync(clientSettings, new DownloadSubmission(payload, key, hashes.Primary, destinationName), CancellationToken.None));
        if (submitted.IsSuccess)
        {
            return submitted.Value!.ClientJobId is { } clientJobId
                ? SendOutcome.Submitted(clientJobId)
                : await ConfirmAsync(client, downloadClient, clientSettings, hashes);
        }

        if (MaySubmit(submitted.Status) && hashes.IsKnown)
        {
            // House rule 8: the answer that never came is not a no. The client's list says whether it took it —
            // looked at once more after ConfirmationRetryDelay when it isn't listed yet, as after a submission
            // the client answered: a qBittorrent before 5.2 lists a torrent only once it has loaded it, a moment
            // after it took it. A list the client couldn't give leaves the job failed with the submission's
            // sentence (specification §3.2, step 6).
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var held = await FindAsync(client, downloadClient, clientSettings, hashes);
                if (!held.IsSuccess)
                    break;

                var match = ClientJobCorrelation.Match(held.Value!, hashes);
                if (match.Status == ClientJobMatchStatus.Found)
                    return SendOutcome.Submitted(match.Job!.ClientJobId);
                if (match.Status == ClientJobMatchStatus.Ambiguous)
                    return SendOutcome.NeedsLinking;
                if (attempt == 1)
                    await Task.Delay(ConfirmationRetryDelay, time, CancellationToken.None);
            }
        }

        return Failed(client, DownloadJobErrorOrigin.DownloadClient, "submission", submitted.Status, submitted.Error!, clientSettings);
    }

    /// <summary>
    /// The client's job a submission it took without an id became (specification §3.2, step 6): looked for
    /// by the payload's info hashes, and once more after <see cref="ConfirmationRetryDelay"/> when it isn't
    /// listed yet. Ambiguous, or no hash to look for at all, needs linking by hand; not listed, or a list the
    /// client couldn't give, is taken without an id — the client said it took it.
    /// </summary>
    private async Task<SendOutcome> ConfirmAsync(
        IntegrationConnectionEntity client, IDownloadClient downloadClient, IntegrationConnectionSettings settings, TorrentInfoHashes hashes)
    {
        if (!hashes.IsKnown)
            return SendOutcome.NeedsLinking;

        for (var attempt = 1; ; attempt++)
        {
            var held = await FindAsync(client, downloadClient, settings, hashes);
            if (!held.IsSuccess)
                return SendOutcome.Submitted(null);

            var match = ClientJobCorrelation.Match(held.Value!, hashes);
            switch (match.Status)
            {
                case ClientJobMatchStatus.Found:
                    return SendOutcome.Submitted(match.Job!.ClientJobId);
                case ClientJobMatchStatus.Ambiguous:
                    return SendOutcome.NeedsLinking;
                case ClientJobMatchStatus.None when attempt == 1:
                    await Task.Delay(ConfirmationRetryDelay, time, CancellationToken.None);
                    break;
                default:
                    return SendOutcome.Submitted(null);
            }
        }
    }

    /// <summary>
    /// MAM-38 — the overwrite check, which never throws but for a bug: one nobody foresaw — a file too odd to read,
    /// memory a piece couldn't get — costs this job its send, never the request, and the job must still be finished.
    /// <c>null</c> then, and nothing is sent: a check that couldn't run can't say the release lands on nothing.
    /// Error, with the type and the stack, never the message, which can quote a path or what the client sent.
    /// </summary>
    private async Task<DownloadOverwriteVerdict?> CheckOverwritesAsync(
        Guid jobId,
        ReleasePayload payload,
        CheckedClientDestination destination,
        string destinationName,
        IntegrationConnectionEntity client,
        IDownloadClient downloadClient,
        IntegrationConnectionSettings settings,
        bool replacement)
    {
        try
        {
            return await overwrites.CheckAsync(
                payload, destination, destinationName, client, downloadClient, settings, anyFileRefuses: replacement, cancellationToken: CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "The overwrite check of download job {JobId} to integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                jobId,
                client.Id,
                client.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return null;
        }
    }

    /// <summary>MAM-24 — the app's sentence for a replacement whose category's folder isn't inside a library the app
    /// knows: nothing reached the client.</summary>
    private static Message ReplacementOutsideLibrariesError(DestinationWording wording) =>
        wording.Sentence("downloads.sender.outside-libraries-destination", "downloads.sender.outside-libraries-category");

    /// <summary>MAM-38 — the app's sentence for a send whose overwrite check broke: nothing reached the client.</summary>
    private static Message OverwriteCheckFailedError(DestinationWording wording) =>
        wording.Sentence("downloads.sender.overwrite-check-failed-destination", "downloads.sender.overwrite-check-failed-category");

    /// <summary>MAM-30 — the app's sentence for a folder destination its client can't use any more: it names the
    /// destination, never the folder (D13), and the client's own sentence is on the Destinations page. The overwrite
    /// check refuses such a folder with it too.</summary>
    internal static Message FolderUnusableError(IntegrationConnectionEntity client, string destinationName) =>
        Message.Of("downloads.sender.folder-unusable", ("client", client.Name), ("destination", destinationName));

    private Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> FindAsync(
        IntegrationConnectionEntity client, IDownloadClient downloadClient, IntegrationConnectionSettings settings, TorrentInfoHashes hashes) =>
        CallAsync(client, "job lookup", () => downloadClient.FindJobsAsync(settings, hashes.All, CancellationToken.None));

    /// <summary>
    /// Whether a failed submission may still have reached the client: no answer in time, the connection lost
    /// on the way, a server error, or an answer the app couldn't read. A refusal, refused credentials or a
    /// version the app doesn't take mean the client added nothing.
    /// </summary>
    private static bool MaySubmit(IntegrationCallStatus status) =>
        status is IntegrationCallStatus.TimedOut
            or IntegrationCallStatus.Unreachable
            or IntegrationCallStatus.ServiceError
            or IntegrationCallStatus.UnexpectedResponse;

    /// <summary>
    /// One adapter call that never throws but for a bug: an adapter answers an expected failure as a result,
    /// so an exception is something nobody foresaw, and it costs this job its outcome, never the request — the
    /// job must still be finished. Error, with the type and the stack, never the message: a parser's message
    /// can quote what the service sent (<c>ReleaseSources</c>). Read as an answer the app couldn't read, which
    /// for a submission means it may have been taken.
    /// </summary>
    private async Task<IntegrationCallResult<T>> CallAsync<T>(IntegrationConnectionEntity integration, string call, Func<Task<IntegrationCallResult<T>>> send)
        where T : class
    {
        try
        {
            return await send();
        }
        catch (Exception exception)
        {
            logger.LogError(
                "The {Call} of a download through integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                call,
                integration.Id,
                integration.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return IntegrationCallResult<T>.Failed(IntegrationCallStatus.UnexpectedResponse, IntegrationAdapterMessages.UnreadableAnswer(integration.Name));
        }
    }

    /// <summary>A call to <paramref name="integration"/> that failed, as the job's outcome: its sentence, which
    /// can name the service's address, so <paramref name="origin"/> marks it as the service's.</summary>
    private SendOutcome Failed(
        IntegrationConnectionEntity integration,
        DownloadJobErrorOrigin origin,
        string call,
        IntegrationCallStatus status,
        Message error,
        IntegrationConnectionSettings settings)
    {
        // Information: the status only — never the address or the sentence (the connection test's rule).
        logger.LogInformation(
            "The {Call} of a download through integration {IntegrationId} ({AdapterType}) failed: {Status}.",
            call,
            integration.Id,
            integration.AdapterType,
            status);
        // Masked again after the adapter (ADR 0007's second pass), which also covers an adapter that isn't HTTP.
        return SendOutcome.Failed(error.Redacted(settings.Secrets), origin);
    }

    /// <summary>A client that doesn't take what the release turned out to be once fetched. MAM-47 — a sentence per kind,
    /// as the kind is the object of "take" (design §1.3).</summary>
    private static Message TurnedOutError(IntegrationConnectionEntity client, ReleasePayloadKind kind) => kind switch
    {
        ReleasePayloadKind.TorrentFile => Message.Of("downloads.sender.turned-out.torrent-file", ("client", client.Name)),
        ReleasePayloadKind.Magnet => Message.Of("downloads.sender.turned-out.magnet", ("client", client.Name)),
        _ => Message.Of("downloads.sender.turned-out.nzb", ("client", client.Name)),
    };

    /// <summary>How the sending ended, to write onto the job.</summary>
    private sealed record SendOutcome(DownloadJobStatus Status, string? ClientJobId, Message? Error, DownloadJobErrorOrigin? ErrorOrigin)
    {
        public static SendOutcome NeedsLinking { get; } = new(DownloadJobStatus.NeedsLinking, null, null, null);

        public static SendOutcome Submitted(string? clientJobId) => new(DownloadJobStatus.Submitted, clientJobId, null, null);

        /// <summary>A failure in <paramref name="error"/>, the app's own sentence unless <paramref name="origin"/>
        /// says whose it is.</summary>
        public static SendOutcome Failed(Message error, DownloadJobErrorOrigin origin = DownloadJobErrorOrigin.App) =>
            new(DownloadJobStatus.Failed, null, error, origin);
    }
}
