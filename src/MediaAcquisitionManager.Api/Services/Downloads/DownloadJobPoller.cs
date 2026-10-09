using MediaAcquisitionManager.Api.Services.Integrations;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <inheritdoc cref="IDownloadJobPoller"/>
/// <remarks>
/// <para>
/// MAM-14 — per client, in this order: an interrupted send with no hash to look for ends
/// <see cref="DownloadJobStatus.Failed"/>, whatever the client; then the client must be there, switched on, of
/// a type this build has an adapter for, and with settings the app can read, or every followed job of it shows
/// why (the app's sentence) and keeps its status; then one lookup of every job by its stored id and info hashes
/// (in batches of <see cref="LookupBatchSize"/>), a failure of which is the client's own sentence on every job,
/// masked again — a client that couldn't be asked never makes a job missing. A job its ids don't find, sent with
/// a v1 hash only, is looked for once more among its destination's jobs: qBittorrent built on libtorrent 2
/// renames such a hybrid torrent to its v2 hash once a magnet's metadata arrives (ADR 0013's gap).
/// </para>
/// <para>
/// A job found takes what the client says (<see cref="DownloadJobFollowing.Observe"/>), the client's current
/// id, and the v2 hash the client lists when it had none; its files are read when it has none stored yet or
/// its status or progress moved, so a finished, idle or paused job isn't read again every pass. They are read
/// before anything is written, then written with the job's row in one transaction, and only while the job
/// still has the status the pass read: a reader never sees a job ahead of its files, and a cancel that got
/// there first leaves both as they are. A job found twice needs linking by hand. A job not found keeps its status and shows why — no longer listed (MAM-14's
/// decision 3), or not listed yet once <see cref="ListingGrace"/> is over — and an interrupted send not found
/// ends <see cref="DownloadJobStatus.Failed"/>.
/// </para>
/// <para>
/// A client that stops answering after the lookup — a files read or a listing to it timed out, couldn't reach
/// it, or its adapter threw — is asked nothing more in that pass: the files of its other jobs stay as stored,
/// their observations are still written, and a job that needs its destination listed shows that call's
/// sentence, as when the listing itself fails. Each call would otherwise wait out its own timeout, and the
/// clients after it with it; the next pass's lookup says whether it answers again.
/// </para>
/// <para>
/// Known gap, left on purpose: a send interrupted between recording its hashes and asking the client whether it
/// already had the torrent (MAM-13's decision 4) is adopted when the client has it — the poller can't tell a
/// torrent it took from one it already had. Rare, and written down rather than handled (ADR 0014). MAM-24 — a
/// replacement never follows such a job: nothing checked where its files land, so <c>IReplacementJobFollower</c> fails
/// the replacement it carries (R6).
/// </para>
/// <para>
/// Information: a job's id and its statuses, and per client what can't be followed — never a title, a path, an
/// address or a sentence, which can quote what a client answered.
/// </para>
/// </remarks>
public sealed class DownloadJobPoller(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IIntegrationSettingsFactory settingsFactory,
    TimeProvider time,
    ILogger<DownloadJobPoller> logger) : IDownloadJobPoller
{
    /// <summary>
    /// How old a job still <see cref="DownloadJobStatus.Created"/> must be before the poller takes it for a send
    /// a stop interrupted (ADR 0013's gap): far longer than any send — the payload's fetch and at most about ten
    /// requests to the client, each bounded at 15 s (<c>IntegrationsInfrastructureExtensions</c>) — so the poller
    /// never races a <see cref="DownloadSender"/> still working. The sender's own writes are conditional on
    /// <see cref="DownloadJobStatus.Created"/> too, so whichever writes first wins.
    /// </summary>
    public static readonly TimeSpan InterruptedSendAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a job the client took without an id may go unlisted before it shows why: a qBittorrent before
    /// 5.2 lists a torrent only once libtorrent has loaded it, a moment after it took it. A minute covers that
    /// with room to spare; past it, something else is wrong and the person should know.
    /// </summary>
    public static readonly TimeSpan ListingGrace = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Ids per lookup. An id is 40 to 64 characters in the query string, so 50 keep one request within a few
    /// KB — inside the 8 KB request line many servers and proxies cap — however many jobs a client has.
    /// </summary>
    public const int LookupBatchSize = 50;

    /// <summary>A v2 info hash in hexadecimal: the entry of a client job's hashes that is one.</summary>
    private const int V2HashLength = 64;

    public async Task PollAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var interruptedBefore = now - InterruptedSendAge;
        // The statuses inlined as constants: they are then IX_DownloadJobs_Followed's own filter, which SQLite
        // uses for a query only when it names them. Oldest first, so a pass reads the same way every time.
        var jobs = await db.DownloadJobs
            .AsNoTracking()
            .Where(job => EF.Constant(DownloadJobFollowing.FollowedStatuses).Contains(job.Status)
                && (job.Status != DownloadJobStatus.Created || job.CreatedUtc <= interruptedBefore))
            .OrderBy(job => job.CreatedUtc)
            .ThenBy(job => job.Id)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0)
            return;

        // Which jobs have files stored, in one query rather than one per job.
        var jobIds = jobs.Select(job => job.Id).ToList();
        var withFiles = (await db.DownloadJobFiles
                .AsNoTracking()
                .Where(file => jobIds.Contains(file.DownloadJobId))
                .Select(file => file.DownloadJobId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var clients = jobs.GroupBy(job => job.DownloadClientConnectionId).ToList();
        var changes = 0;
        foreach (var clientJobs in clients)
        {
            // One client each: a bug, or a database hiccup, while following one client's jobs costs that client
            // this pass, never the others'.
            try
            {
                changes += await FollowAsync(clientJobs.Key, [.. clientJobs], withFiles, now, cancellationToken);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                logger.LogError(exception, "Following the download jobs of integration {IntegrationId} threw; the next pass tries again.", clientJobs.Key);
            }
        }

        // Debug, not Information: a pass runs every 2 s while anything is followed.
        logger.LogDebug(
            "Followed {Count} download jobs of {Clients} download clients; {Changes} changed status.", jobs.Count, clients.Count, changes);
    }

    // ── one client ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Follows <paramref name="jobs"/>, all of the client <paramref name="clientId"/>; how many changed
    /// status.</summary>
    private async Task<int> FollowAsync(
        Guid? clientId, List<DownloadJobEntity> jobs, HashSet<Guid> withFiles, DateTime now, CancellationToken cancellationToken)
    {
        var changes = 0;
        var followed = new List<DownloadJobEntity>();
        foreach (var job in jobs)
        {
            // A send interrupted before it knew what it sent has nothing to look for, so it can never be told
            // apart in the client, whatever the client's state.
            if (job.Status == DownloadJobStatus.Created && !HashesOf(job).IsKnown)
            {
                if (await StopAsync(job, DownloadJobStatus.Failed, InterruptedBeforeHashesError(job.DownloadClientName), now))
                    changes++;
            }
            else
            {
                followed.Add(job);
            }
        }

        if (followed.Count == 0)
            return changes;

        var client = clientId is { } id
            ? await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
            : null;
        if (client is null)
        {
            // SetNull: the client was removed — or is being, this very moment. The job keeps its name.
            var removed = 0;
            foreach (var job in followed)
                removed += await BlockAsync(job, ClientRemovedReason(job.DownloadClientName), DownloadJobErrorOrigin.App) ? 1 : 0;
            if (removed > 0)
                logger.LogInformation("{Count} followed download jobs lost their download client; they can't be followed any more.", removed);
            return changes;
        }

        var (context, refusal, refusalKind) = Prepare(client);
        if (context is null)
        {
            await BlockAllAsync(client, followed, refusal!, DownloadJobErrorOrigin.App, refusalKind);
            return changes;
        }

        // ── the lookup ──
        var ids = followed
            .SelectMany(job => ClientJobCorrelation.FollowIds(job.ClientJobId, HashesOf(job)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var listed = new List<ClientJob>();
        foreach (var batch in ids.Chunk(LookupBatchSize))
        {
            var found = await CallAsync(context, "job lookup", () => context.DownloadClient.FindJobsAsync(context.Settings, batch, cancellationToken), cancellationToken);
            if (found is not { IsSuccess: true })
            {
                // Never "missing": a client that couldn't be asked says nothing about any job.
                var (reason, origin, kind) = ReasonFor(context, found);
                await BlockAllAsync(client, followed, reason, origin, kind);
                return changes;
            }

            listed.AddRange(found.Value!);
        }

        // ── each job ──
        var destinations = new Dictionary<string, IntegrationCallResult<IReadOnlyList<ClientJob>>?>(StringComparer.Ordinal);
        foreach (var job in followed)
        {
            var hashes = HashesOf(job);
            var match = ClientJobCorrelation.Follow(listed, job.ClientJobId, hashes);
            if (match.Status == ClientJobMatchStatus.None && MayHaveBeenRenamed(job))
            {
                // Once per destination and pass, however many of its jobs need it.
                var key = job.ClientDestinationKey!;
                if (!destinations.TryGetValue(key, out var inDestination))
                {
                    // Not asked again in a pass it stopped answering in: as if this listing had failed so.
                    if (context.Unanswered is { } unanswered)
                    {
                        await BlockAsync(job, unanswered.Reason, unanswered.Origin);
                        continue;
                    }

                    inDestination = await CallAsync(
                        context, "destination listing", () => context.DownloadClient.ListDestinationJobsAsync(context.Settings, key, cancellationToken), cancellationToken);
                    destinations[key] = inDestination;
                    NoteUnanswered(context, inDestination);
                }

                if (inDestination is not { IsSuccess: true })
                {
                    var (reason, origin, _) = ReasonFor(context, inDestination);
                    await BlockAsync(job, reason, origin);
                    continue;
                }

                match = ClientJobCorrelation.Follow(inDestination.Value!, job.ClientJobId, hashes);
            }

            var changed = match.Status switch
            {
                ClientJobMatchStatus.Found => await ObserveAsync(context, job, match.Job!, withFiles, now, cancellationToken),
                // Two of the client's jobs answer to this one's ids: which it is has to be picked by hand, as a
                // send that can't tell does (MAM-13).
                ClientJobMatchStatus.Ambiguous => await StopAsync(job, DownloadJobStatus.NeedsLinking, null, now),
                _ => await NotListedAsync(client, job, now),
            };
            if (changed)
                changes++;
        }

        return changes;
    }

    /// <summary>
    /// Whether <paramref name="client"/> can be asked about its jobs: the settings to ask it with, or the app's
    /// sentence saying why not and a word for the log. The order a send checks in (<c>DownloadService</c>).
    /// </summary>
    private (ClientContext? Context, string? Refusal, string Kind) Prepare(IntegrationConnectionEntity client)
    {
        if (!client.IsEnabled)
            return (null, SwitchedOffReason(client.Name), "SwitchedOff");
        if (adapters.Find<IDownloadClient>(client.AdapterType) is not { } downloadClient)
            return (null, NoAdapterReason(client.AdapterType), "AdapterUnavailable");

        var settings = settingsFactory.Create(client);
        return settings.Settings is { } built
            ? (new ClientContext(client, downloadClient, built), null, "")
            : (null, settings.Error!, "SecretUnreadable");
    }

    // ── one job ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Brings <paramref name="job"/> in line with <paramref name="clientJob"/>, the client's job it was matched
    /// to, and reads its files when they may have changed; whether its status changed.
    /// </summary>
    private async Task<bool> ObserveAsync(
        ClientContext context, DownloadJobEntity job, ClientJob clientJob, HashSet<Guid> withFiles, DateTime now, CancellationToken cancellationToken)
    {
        var observation = DownloadJobFollowing.Observe(job.Status, clientJob);
        var progress = observation.Progress ?? job.Progress;
        // A job sent without its v2 hash keeps the one the client lists once it knows it: what finds the job
        // after the client renames it to it.
        var infoHashV2 = job.InfoHashV2 ?? clientJob.InfoHashes.FirstOrDefault(hash => hash.Length == V2HashLength);
        // MAM-14's decision 2: a reason, never an end, and gone once the client stops reporting it.
        var reason = observation.ClientReportsError ? ClientErrorReason(context.Client.Name) : null;
        DownloadJobErrorOrigin? origin = reason is null ? null : DownloadJobErrorOrigin.App;
        var moved = observation.Status != job.Status || progress != job.Progress;

        // Written only when something differs: a pass over a job nothing happened to writes nothing.
        var rowDiffers = moved
            || !string.Equals(clientJob.ClientJobId, job.ClientJobId, StringComparison.Ordinal)
            || !string.Equals(infoHashV2, job.InfoHashV2, StringComparison.Ordinal)
            || !string.Equals(DownloadJobUpdates.Stored(reason), job.BlockReason, StringComparison.Ordinal)
            || origin != job.BlockReasonOrigin;
        Task<int> RecordAsync() => db.DownloadJobs.RecordObservationAsync(
            job.Id, job.Status, observation.Status, progress, clientJob.ClientJobId, infoHashV2, reason, origin, now);

        // The files first, from the client — never between the job's write and theirs, which a reader would
        // otherwise see apart: a job Downloaded with the files of the pass before, for good, as an ended job
        // isn't read again. None read — not needed, failed, or a client that stopped answering — keeps the
        // stored ones, and the observation is written all the same.
        var files = (moved || !withFiles.Contains(job.Id)) && context.Unanswered is null
            ? await ReadFilesAsync(context, clientJob.ClientJobId, cancellationToken)
            : null;
        var written = files is null
            ? !rowDiffers || await RecordAsync() == 1
            : await RecordWithFilesAsync(job, rowDiffers ? RecordAsync : null, files, cancellationToken);
        // Cancelled meanwhile: it isn't followed any more, and neither its row nor its files were written.
        if (!written)
            return false;

        if (observation.Status == job.Status)
            return false;

        LogChange(job.Id, job.Status, observation.Status);
        return true;
    }

    /// <summary>
    /// What a job the client answered about but doesn't list comes to: an interrupted send ends
    /// <see cref="DownloadJobStatus.Failed"/>; any other keeps its status and is still followed, with why it
    /// isn't moving on — none while a job taken without an id is within <see cref="ListingGrace"/>. Whether its
    /// status changed.
    /// </summary>
    private async Task<bool> NotListedAsync(IntegrationConnectionEntity client, DownloadJobEntity job, DateTime now)
    {
        if (job.Status == DownloadJobStatus.Created)
            return await StopAsync(job, DownloadJobStatus.Failed, InterruptedNotListedError(client.Name), now);

        // A job the client listed before (it has the client's id) is gone from it: MAM-14's decision 3 — still
        // followed, in case it comes back with the same hash.
        var reason = job.ClientJobId is not null
            ? MissingReason(client.Name)
            : now - job.StatusChangedUtc < ListingGrace
                ? null
                : NotListedReason(client.Name);
        await BlockAsync(job, reason, reason is null ? null : DownloadJobErrorOrigin.App);
        return false;
    }

    /// <summary>
    /// The client's list of the files of its job <paramref name="clientJobId"/>, or <c>null</c> when the read
    /// failed — the torrent gone between the lookup and this read, which qBittorrent answers as not found, or
    /// anything else: why a job isn't moving on comes from the lookup, never from here, and the stored files
    /// stay. A magnet still fetching its metadata lists none, stored as none and read again next pass.
    /// </summary>
    private async Task<IReadOnlyList<ClientJobFile>?> ReadFilesAsync(ClientContext context, string clientJobId, CancellationToken cancellationToken)
    {
        var read = await CallAsync(
            context, "files read", () => context.DownloadClient.ListJobFilesAsync(context.Settings, clientJobId, cancellationToken), cancellationToken);
        NoteUnanswered(context, read);
        return read is { IsSuccess: true } ? read.Value! : null;
    }

    // ── writes ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes what the pass read about <paramref name="job"/> — its row through <paramref name="recordAsync"/>,
    /// when that differs, and <paramref name="files"/>, when they differ from the stored ones — in one
    /// transaction, and only while the job still has the status the pass read: the row's write is conditional
    /// on it, and with no row to write, an <c>UPDATE</c> that changes nothing claims the job
    /// (<see cref="DownloadJobUpdates.ClaimAsync"/>). A cancel that got there first leaves both as they are.
    /// Whether the job was still followed.
    /// </summary>
    /// <remarks>
    /// The files are replaced by position: updated in place, never moved, so the unique (job, position) index
    /// never meets two rows in one place within the save. The stored ones are read before the transaction:
    /// only the poller writes them, and its passes never overlap. Never cancelled once under way, as the job's
    /// own writes aren't: what was read is written.
    /// </remarks>
    private async Task<bool> RecordWithFilesAsync(
        DownloadJobEntity job, Func<Task<int>>? recordAsync, IReadOnlyList<ClientJobFile> files, CancellationToken cancellationToken)
    {
        try
        {
            var stored = (await db.DownloadJobFiles
                    .AsTracking()
                    .Where(file => file.DownloadJobId == job.Id)
                    .ToListAsync(cancellationToken))
                .ToDictionary(file => file.Position);
            for (var position = 0; position < files.Count; position++)
            {
                var file = files[position];
                if (stored.TryGetValue(position, out var row))
                {
                    if (!string.Equals(row.RelativePath, file.RelativePath, StringComparison.Ordinal))
                        row.RelativePath = file.RelativePath;
                    if (row.SizeBytes != file.SizeBytes)
                        row.SizeBytes = file.SizeBytes;
                    if (row.Progress != file.Progress)
                        row.Progress = file.Progress;
                    if (row.IsWanted != file.IsWanted)
                        row.IsWanted = file.IsWanted;
                }
                else
                {
                    db.DownloadJobFiles.Add(new DownloadJobFileEntity
                    {
                        DownloadJobId = job.Id,
                        Position = position,
                        RelativePath = file.RelativePath,
                        SizeBytes = file.SizeBytes,
                        Progress = file.Progress,
                        IsWanted = file.IsWanted,
                    });
                }
            }

            foreach (var gone in stored.Values.Where(row => row.Position >= files.Count))
                db.DownloadJobFiles.Remove(gone);

            var filesDiffer = db.ChangeTracker.HasChanges();
            if (recordAsync is null && !filesDiffer)
                return true;

            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var written = recordAsync is not null
                ? await recordAsync()
                : await db.DownloadJobs.ClaimAsync(job.Id, job.Status);
            // Rolled back as it is disposed: nothing was written.
            if (written == 0)
                return false;

            if (filesDiffer)
                await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            return true;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Ends the following of <paramref name="job"/> as <paramref name="status"/>, with the app's
    /// <paramref name="error"/> if any; whether it did — a cancel or a send that got there first wins.</summary>
    private async Task<bool> StopAsync(DownloadJobEntity job, DownloadJobStatus status, string? error, DateTime now)
    {
        if (await db.DownloadJobs.StopFollowingAsync(job.Id, job.Status, status, error, now) == 0)
            return false;

        LogChange(job.Id, job.Status, status);
        return true;
    }

    /// <summary>Writes why <paramref name="job"/> isn't moving on, or that nothing blocks it, when that differs
    /// from what it shows; whether it wrote.</summary>
    private async Task<bool> BlockAsync(DownloadJobEntity job, string? reason, DownloadJobErrorOrigin? origin) =>
        (!string.Equals(DownloadJobUpdates.Stored(reason), job.BlockReason, StringComparison.Ordinal) || origin != job.BlockReasonOrigin)
        && await db.DownloadJobs.RecordBlockReasonAsync(job.Id, job.Status, reason, origin) == 1;

    /// <summary>
    /// Writes why every one of <paramref name="jobs"/> can't be followed — their client can't be asked — and,
    /// when that is news for any of them, says so in the log once: what can't be followed and why, in a word,
    /// never the sentence.
    /// </summary>
    private async Task BlockAllAsync(
        IntegrationConnectionEntity client, List<DownloadJobEntity> jobs, string reason, DownloadJobErrorOrigin origin, string kind)
    {
        var written = 0;
        foreach (var job in jobs)
            written += await BlockAsync(job, reason, origin) ? 1 : 0;
        if (written > 0)
        {
            logger.LogInformation(
                "The download jobs of integration {IntegrationId} ({AdapterType}) can't be followed for now: {Failure}.",
                client.Id,
                client.AdapterType,
                kind);
        }
    }

    private void LogChange(Guid jobId, DownloadJobStatus from, DownloadJobStatus to) =>
        logger.LogInformation("Download job {JobId} went from {From} to {To}.", jobId, from, to);

    // ── calls ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One call to the client that never throws but for the caller's cancellation: <c>null</c> when the adapter
    /// threw, which is a bug nobody foresaw. Error, with the type and the stack, never the message — a parser's
    /// message can quote what the client sent (<see cref="DownloadSender"/>'s rule).
    /// </summary>
    private async Task<IntegrationCallResult<T>?> CallAsync<T>(
        ClientContext context, string call, Func<Task<IntegrationCallResult<T>>> send, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await send();
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogError(
                "The {Call} of the followed download jobs of integration {IntegrationId} ({AdapterType}) threw {ExceptionType} {StackTrace}",
                call,
                context.Client.Id,
                context.Client.AdapterType,
                exception.GetType().FullName,
                exception.StackTrace);
            return null;
        }
    }

    /// <summary>
    /// Why a call that failed leaves its jobs where they are: the client's own sentence, masked again after the
    /// adapter (ADR 0007's second pass), which can name its address and so is the owner's to read; or the app's
    /// for an adapter that threw. With a word for the log.
    /// </summary>
    private static (string Reason, DownloadJobErrorOrigin Origin, string Kind) ReasonFor<T>(ClientContext context, IntegrationCallResult<T>? failed)
        where T : class =>
        failed is null
            ? (ThrewReason(context.Client.Name), DownloadJobErrorOrigin.App, "Threw")
            : (SecretRedaction.Redact(failed.Error, context.Settings.Secrets)!, DownloadJobErrorOrigin.DownloadClient, failed.Status.ToString());

    /// <summary>
    /// Notes on <paramref name="context"/> that its client stopped answering in this pass, with why, when
    /// <paramref name="call"/> — a files read or a destination listing — timed out, couldn't reach it, or its
    /// adapter threw; the first such call's reason is kept. Any other failure is the one call's: the client
    /// answered.
    /// </summary>
    private static void NoteUnanswered<T>(ClientContext context, IntegrationCallResult<T>? call)
        where T : class
    {
        if (context.Unanswered is null
            && (call is null || call.Status is IntegrationCallStatus.TimedOut or IntegrationCallStatus.Unreachable))
        {
            var (reason, origin, _) = ReasonFor(context, call);
            context.Unanswered = (reason, origin);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static TorrentInfoHashes HashesOf(DownloadJobEntity job) => new(job.InfoHashV1, job.InfoHashV2);

    /// <summary>
    /// Whether the client may have renamed <paramref name="job"/> past what its ids find (ADR 0013's gap): sent
    /// knowing only its v1 hash, into a destination that can be listed.
    /// </summary>
    private static bool MayHaveBeenRenamed(DownloadJobEntity job) =>
        job.InfoHashV1 is not null && job.InfoHashV2 is null && !string.IsNullOrWhiteSpace(job.ClientDestinationKey);

    // The reasons a job shows. Each names the client — by its name now, which is what Integrations lists — and
    // says what happens next.

    private static string ClientRemovedReason(string clientName) =>
        $"The download client “{clientName}” was removed, so this download can't be followed. Cancel the job to stop tracking it.";

    private static string SwitchedOffReason(string clientName) =>
        $"“{clientName}” is switched off, so this download isn't followed for now. The owner can switch it on under Integrations.";

    private static string NoAdapterReason(string adapterType) => $"This version of the app can't follow downloads in “{adapterType}”.";

    private static string ThrewReason(string clientName) => $"The app couldn't read what “{clientName}” answered.";

    private static string ClientErrorReason(string clientName) =>
        $"“{clientName}” reports an error for this download, so it isn't moving on. Check it in “{clientName}”: the job goes on by itself once the error clears.";

    private static string MissingReason(string clientName) =>
        $"“{clientName}” doesn't list this download any more. It is still followed in case it comes back; to send the release again, cancel this job first.";

    private static string NotListedReason(string clientName) =>
        $"“{clientName}” took this download but hasn't listed it since. It is still followed; to send the release again, cancel this job first.";

    // ADR 0013's gap: a job a stop left Created. Errors, not reasons: following ends.

    private static string InterruptedBeforeHashesError(string clientName) =>
        $"The app stopped while sending this download, before it could tell whether “{clientName}” took it, so it isn't followed. Check “{clientName}” before sending the release again.";

    private static string InterruptedNotListedError(string clientName) =>
        $"The app stopped while sending this download, and “{clientName}” doesn't list it, so it isn't followed. Send the release again if you still want it.";

    /// <summary>A client that can be asked: its row, its adapter and the settings to ask it with, for one
    /// pass.</summary>
    private sealed record ClientContext(IntegrationConnectionEntity Client, IDownloadClient DownloadClient, IntegrationConnectionSettings Settings)
    {
        /// <summary>
        /// MAM-14 — why the client stopped answering in this pass, once a files read or a destination listing
        /// to it timed out, couldn't reach it, or its adapter threw (<see cref="NoteUnanswered{T}"/>); <c>null</c>
        /// while it answers. No further such call is made to it in the pass.
        /// </summary>
        public (string Reason, DownloadJobErrorOrigin Origin)? Unanswered { get; set; }
    }
}
