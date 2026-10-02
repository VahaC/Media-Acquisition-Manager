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
    IEncryptionService encryption,
    TimeProvider time,
    ILogger<DownloadSender> logger) : IDownloadSender
{
    /// <summary>
    /// How long to wait before looking a second time for a torrent the client took but didn't list. A
    /// qBittorrent before 5.2 answers <c>Ok.</c> without an id and adds the torrent a moment later, once
    /// libtorrent has loaded it; half a second covers that on a LAN, and a torrent still not listed after it
    /// is recorded as taken, with its info hash for MAM-14 to find it by.
    /// </summary>
    private static readonly TimeSpan ConfirmationRetryDelay = TimeSpan.FromMilliseconds(500);

    private const string LocatorUnreadableError =
        "The release's download link can't be read with this installation's encryption key, so nothing was sent. Search again.";

    public async Task SendAsync(
        Guid jobId,
        ReleaseEntity release,
        IntegrationConnectionEntity source,
        IntegrationConnectionEntity client,
        string clientDestinationKey)
    {
        var outcome = await RunAsync(jobId, release, source, client, clientDestinationKey);
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
        string key)
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
        if (!downloadClient.Capabilities.PayloadKinds.Contains(payload.Kind))
            return SendOutcome.Failed($"“{client.Name}” can't take {KindOf(payload.Kind)}, which this release turned out to be, so nothing was sent.");

        var hashes = TorrentInfoHashReader.Read(payload);
        if (hashes.IsKnown)
            await db.DownloadJobs.RecordInfoHashesAsync(jobId, hashes);

        // ── the destination ──
        // Through the Destinations' own listing, which leaves a switched-off client alone and masks what
        // it says, as every read of a client's categories does.
        var listed = await destinations.ListAsync(client, CancellationToken.None);
        if (listed.Status != ClientDestinationsStatus.Listed)
        {
            // The client's own sentence when it was asked and failed; the app's when it wasn't asked at all.
            return listed.Status == ClientDestinationsStatus.CallFailed
                ? SendOutcome.Failed(listed.Error!, DownloadJobErrorOrigin.DownloadClient)
                : SendOutcome.Failed(listed.Error!);
        }
        if (!listed.Destinations.Any(candidate => string.Equals(candidate.Destination.Key, key, StringComparison.Ordinal)))
        {
            return SendOutcome.Failed(
                $"“{client.Name}” has no category “{key}” any more, so nothing was sent: it would have created one in its default folder. Create the category again in “{client.Name}”, or the owner edits the destination.");
        }

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
                return SendOutcome.Failed($"“{client.Name}” already has this torrent, so nothing was sent.");
        }

        // ── the one submission ──
        var submitted = await CallAsync(
            client,
            "submission",
            () => downloadClient.SubmitAsync(clientSettings, new DownloadSubmission(payload, key, hashes.Primary), CancellationToken.None));
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
            return IntegrationCallResult<T>.Failed(IntegrationCallStatus.UnexpectedResponse, $"The app couldn't read what “{integration.Name}” answered.");
        }
    }

    /// <summary>A call to <paramref name="integration"/> that failed, as the job's outcome: its sentence, which
    /// can name the service's address, so <paramref name="origin"/> marks it as the service's.</summary>
    private SendOutcome Failed(
        IntegrationConnectionEntity integration,
        DownloadJobErrorOrigin origin,
        string call,
        IntegrationCallStatus status,
        string error,
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
        return SendOutcome.Failed(SecretRedaction.Redact(error, settings.Secrets)!, origin);
    }

    private static string KindOf(ReleasePayloadKind kind) => kind switch
    {
        ReleasePayloadKind.TorrentFile => "a .torrent file",
        ReleasePayloadKind.Magnet => "a magnet link",
        _ => "an NZB file",
    };

    /// <summary>How the sending ended, to write onto the job.</summary>
    private sealed record SendOutcome(DownloadJobStatus Status, string? ClientJobId, string? Error, DownloadJobErrorOrigin? ErrorOrigin)
    {
        public static SendOutcome NeedsLinking { get; } = new(DownloadJobStatus.NeedsLinking, null, null, null);

        public static SendOutcome Submitted(string? clientJobId) => new(DownloadJobStatus.Submitted, clientJobId, null, null);

        /// <summary>A failure in <paramref name="error"/>, the app's own sentence unless <paramref name="origin"/>
        /// says whose it is.</summary>
        public static SendOutcome Failed(string error, DownloadJobErrorOrigin origin = DownloadJobErrorOrigin.App) =>
            new(DownloadJobStatus.Failed, null, error, origin);
    }
}
