using System.Linq.Expressions;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <inheritdoc cref="IDownloadService"/>
/// <remarks>
/// This service decides what may be sent and writes the job; <see cref="IDownloadSender"/> talks to the
/// source and the client. MAM-14 — it reads and cancels jobs without talking to anyone: what the client says
/// about a job is <see cref="IDownloadJobPoller"/>'s to read.
/// </remarks>
public sealed class DownloadService(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IDownloadSender sender,
    IAuditTrail auditTrail,
    IDownloadMapper mapper,
    TimeProvider time,
    ILogger<DownloadService> logger) : IDownloadService
{
    /// <summary>Another user's release and a forgotten one answer alike: nothing confirms a release exists
    /// in someone else's search (ADR 0011).</summary>
    private const string ReleaseMissingError =
        "This release isn't available any more: a search keeps what it found for a day. Search again.";

    private const string ClientGoneError = "The download client you picked no longer exists. Reload the page and pick one again.";

    private const string DestinationGoneError = "The destination you picked no longer exists. Reload the page and pick one again.";

    private const string RequestIdReusedError =
        "This request id was already used to send a different download. Reload the page and try again.";

    private const string SourceRemovedError =
        "The source that listed this release has been removed, so the release can't be fetched. Search again.";

    // MAM-14 — a cancel of a job that already ended, one sentence per way it ends.
    private const string AlreadyDownloadedError = "This download is already finished, so there's nothing to stop following.";

    private const string AlreadyFailedError = "This download has already failed, so there's nothing to stop following.";

    private const string AlreadyCancelledError = "This download is already cancelled, so there's nothing to stop following.";

    // MAM-14 — a cancel of a Created job younger than DownloadJobPoller.InterruptedSendAge: its send may still be
    // under way, and only an interrupted one can be cancelled.
    private const string StillBeingSentError = "This download is still being sent, so it can't be cancelled yet. Try again in a few minutes.";

    public async Task<DownloadOptionsResult> GetOptionsAsync(string? releaseId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        Guid? id = Guid.TryParse(releaseId, out var parsed) ? parsed : null;
        if (DownloadRules.ValidateReleaseId(id) is { } error)
            return DownloadOptionsResult.Invalid(error);
        if (await FindReleaseAsync(id!.Value, callerUserId, cancellationToken) is not { } release)
            return DownloadOptionsResult.ReleaseMissing(ReleaseMissingError);

        // MAM-13 — read from the database alone: the dialog opens without asking any client, and the
        // category is confirmed with the client when the release is sent. A switched-off client is left
        // out, as a sync and a search leave it (MAM-7's switch), and so is one that couldn't take this
        // release or has nowhere to send it.
        var clients = await db.IntegrationConnections
            .AsNoTracking()
            .Where(client => client.Kind == IntegrationKind.DownloadClient && client.IsEnabled)
            // The normalised name is unique, so the order is total.
            .OrderBy(client => client.NormalizedName)
            .ToListAsync(cancellationToken);
        var clientIds = clients.Select(client => client.Id).ToList();
        var destinations = (await db.DestinationMappings
                .AsNoTracking()
                .Where(destination => clientIds.Contains(destination.DownloadClientConnectionId))
                // A name is unique per client, so the order within one is total.
                .OrderBy(destination => destination.NormalizedName)
                .ToListAsync(cancellationToken))
            .ToLookup(destination => destination.DownloadClientConnectionId);

        var options = new List<DownloadClientOptionDto>();
        foreach (var client in clients)
        {
            if (adapters.Find(client.AdapterType) is not { } adapter || adapter is not IDownloadClient downloadClient)
                continue;
            if (!Takes(downloadClient.Capabilities, release.PayloadKind))
                continue;
            var own = destinations[client.Id].ToList();
            if (own.Count > 0)
                options.Add(mapper.ToOption(client, adapter.Descriptor.DisplayName, own));
        }

        return DownloadOptionsResult.Listed(new DownloadOptionsResponse(options));
    }

    public async Task<DownloadSendResult> SendAsync(SendDownloadRequest request, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // MAM-13 — checked in this order: the ids present; this very request sent before; the release the
        // caller's; the client and the destination there and belonging together; the client usable and
        // able to take the payload; the source still there to fetch from; the release not already sent to
        // the client. One sentence per answer, the first one broken, and nothing written or sent for any.
        if (DownloadRules.Validate(request.RequestId, request.ReleaseId, request.DownloadClientId, request.DestinationId) is { } error)
            return DownloadSendResult.Refused(DownloadSendStatus.Invalid, error);

        var requestId = request.RequestId!.Value;
        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        // First, before anything else is read: a retry is answered with the job its request made even after
        // the release, the client or the destination changed or went (ADR 0013).
        if (await FindRequestAsync(callerUserId, requestId, cancellationToken) is { } earlier)
            return Replay(earlier, request, forOwner);

        if (await FindReleaseAsync(request.ReleaseId!.Value, callerUserId, cancellationToken) is not { } release)
            return DownloadSendResult.Refused(DownloadSendStatus.ReleaseMissing, ReleaseMissingError);
        if (await FindDownloadClientAsync(request.DownloadClientId!.Value, cancellationToken) is not { } client)
            return DownloadSendResult.Refused(DownloadSendStatus.ClientMissing, ClientGoneError);
        if (await db.DestinationMappings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == request.DestinationId, cancellationToken)
            is not { } destination)
            return DownloadSendResult.Refused(DownloadSendStatus.DestinationMissing, DestinationGoneError);
        if (destination.DownloadClientConnectionId != client.Id)
        {
            return DownloadSendResult.Refused(
                DownloadSendStatus.Invalid,
                $"“{destination.Name}” is a destination of another download client. Pick one of “{client.Name}”’s.");
        }

        if (ClientRefusal(client, release.PayloadKind) is { } clientRefusal)
            return clientRefusal;

        var source = release.SourceConnectionId is { } sourceId
            ? await db.IntegrationConnections.AsNoTracking().SingleOrDefaultAsync(row => row.Id == sourceId, cancellationToken)
            : null;
        if (SourceRefusal(source) is { } sourceRefusal)
            return sourceRefusal;

        if (await WasSentAsync(release, client.Id, cancellationToken))
        {
            // A copy of this very request racing this one may have written its job since the lookup above, and
            // that job is the one found here: the copy is answered with it, as the unique index answers a copy
            // that gets as far as the save (ADR 0013), never refused as a release already sent.
            if (await FindRequestAsync(callerUserId, requestId, cancellationToken) is { } racing)
                return Replay(racing, request, forOwner);

            // Decision 3 — the release is recognised by its source's id for it (or its info hash), not by
            // the row: every search stores its own copy of a listing (ADR 0006, MAM-10).
            return DownloadSendResult.Refused(
                DownloadSendStatus.AlreadySent, $"This release was already sent to “{client.Name}”, so it isn't sent twice.");
        }

        var job = await CreateJobAsync(requestId, callerUserId, forOwner, release, client, destination, cancellationToken);
        if (job.Refusal is not null)
            return job.Refusal;

        // The job and its audit row are committed: from here the request is sent once, and its retries
        // answer this job.
        await sender.SendAsync(job.Id, release, source!, client, destination.ClientDestinationKey);

        var sent = await FindJobAsync(job.Id, forOwner, cancellationToken);
        return DownloadSendResult.Created(sent!);
    }

    // Another user's job answers as none, like their searches: nothing confirms it exists.
    public async Task<DownloadJobDto?> GetAsync(Guid jobId, Guid callerUserId, CancellationToken cancellationToken = default) =>
        await JobRows(job => job.Id == jobId && job.RequestedByUserId == callerUserId).SingleOrDefaultAsync(cancellationToken) is { } row
            ? ToDto(row, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken))
            : null;

    public async Task<DownloadJobListResult> ListAsync(
        string? status, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return DownloadJobListResult.Invalid(pagingError);
        if (DownloadJobListRules.Validate(status, out var filter) is { } statusError)
            return DownloadJobListResult.Invalid(statusError);

        // MAM-14's decision 1: the caller's own jobs — the owner's too, as GetAsync answers. A job nobody is
        // recorded as sending (its sender's account removed) is nobody's to list.
        var jobs = db.DownloadJobs.AsNoTracking().Where(job => job.RequestedByUserId == callerUserId);
        if (filter is { } only)
            jobs = jobs.Where(job => job.Status == only);

        var totalCount = await jobs.CountAsync(cancellationToken);
        // Past the last page there is nothing to read; and a page far enough out would overflow the offset,
        // which EF Core takes as an int (the UserAccountService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return DownloadJobListResult.Listed(new DownloadJobListResponse([], totalCount, page, pageSize));

        // The page's ids first — newest first, the id making the order total — then those jobs with their
        // releases through JobRows, put back in the page's order: EF Core can't order by the JobRow it projects
        // into, as it can't filter by it.
        var ids = await jobs
            .OrderByDescending(job => job.CreatedUtc)
            .ThenByDescending(job => job.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
        var rows = (await JobRows(job => ids.Contains(job.Id)).ToListAsync(cancellationToken)).ToDictionary(row => row.Job.Id);
        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        return DownloadJobListResult.Listed(
            new DownloadJobListResponse([.. ids.Select(id => ToDto(rows[id], forOwner))], totalCount, page, pageSize));
    }

    public async Task<DownloadJobFileListResult> ListFilesAsync(
        Guid jobId, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        // The job first, as SearchService checks the search first: another user's answers as none, whatever the
        // page asked for.
        if (!await db.DownloadJobs.AnyAsync(job => job.Id == jobId && job.RequestedByUserId == callerUserId, cancellationToken))
            return DownloadJobFileListResult.NotFound;
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return DownloadJobFileListResult.Invalid(pagingError);

        var files = db.DownloadJobFiles.AsNoTracking().Where(file => file.DownloadJobId == jobId);
        var totalCount = await files.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return DownloadJobFileListResult.Listed(new DownloadJobFileListResponse([], totalCount, page, pageSize));

        // A position is unique per job, so the order is total.
        var items = await files
            .OrderBy(file => file.Position)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return DownloadJobFileListResult.Listed(
            new DownloadJobFileListResponse([.. items.Select(mapper.ToDto)], totalCount, page, pageSize));
    }

    public async Task<DownloadCancelResult> CancelAsync(Guid jobId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await JobRows(job => job.Id == jobId && job.RequestedByUserId == callerUserId).SingleOrDefaultAsync(cancellationToken) is not { } row)
            return DownloadCancelResult.NotFound;

        // MAM-14 — the job and its audit row in one transaction (the UserAccountService.DisableAsync shape), and
        // nothing sent to the client: IDownloadClient has no member that could remove, stop or pause anything
        // (ADR 0014, DownloadClientNeverRemovesTests).
        var cancelled = false;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // Conditional, in one statement: a job that ended since it was read — finished, or cancelled by a
            // click racing this one — is left as it is, and nothing is recorded; so is a Created job whose send
            // may still be under way. A pass of the poller that moved it on meanwhile doesn't stop it: it is as
            // cancellable as before.
            if (await db.DownloadJobs.Where(job => job.RequestedByUserId == callerUserId).CancelAsync(jobId, time.GetUtcNow().UtcDateTime) == 1)
            {
                var actor = await ActorAsync(callerUserId, cancellationToken);
                auditTrail.Record(AuditAction.DownloadCancelled, actor, AuditTarget.DownloadJob(row.Job, row.ReleaseTitle));
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("{ActorUserName} stopped following download job {JobId}.", actor.UserName, jobId);
                cancelled = true;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        db.ChangeTracker.Clear();
        var job = (await FindJobAsync(jobId, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken), cancellationToken))!;
        if (cancelled)
            return DownloadCancelResult.Cancelled(job);

        // Still Created after a write that didn't take it: younger than an interrupted send, so its send is still
        // the sender's — a cancel later, or the sender's own outcome, settles it.
        return job.Status == DownloadJobStatus.Created
            ? DownloadCancelResult.StillBeingSent(StillBeingSentError)
            : DownloadCancelResult.NothingToStop(NothingToStopError(job.Status));
    }

    // ── checks ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether a client of these <paramref name="capabilities"/> can take a release of
    /// <paramref name="kind"/>. A kind the listing didn't state is a gateway's link, which turns out a
    /// .torrent file or a magnet link only when it is fetched: a client taking either may get it, and the
    /// fetched kind is checked again before anything is sent (<see cref="DownloadSender"/>).
    /// </summary>
    private static bool Takes(DownloadClientCapabilities capabilities, ReleasePayloadKind? kind) =>
        kind is { } known
            ? capabilities.PayloadKinds.Contains(known)
            : capabilities.PayloadKinds.Contains(ReleasePayloadKind.TorrentFile) || capabilities.PayloadKinds.Contains(ReleasePayloadKind.Magnet);

    /// <summary>Why <paramref name="client"/> can't be sent a release of <paramref name="kind"/>, or
    /// <c>null</c> when it can.</summary>
    private DownloadSendResult? ClientRefusal(IntegrationConnectionEntity client, ReleasePayloadKind? kind)
    {
        // The order a destination's save refuses in (ADR 0010, decision 3): a switched-off client is left
        // alone; then whether this build can use its type.
        if (!client.IsEnabled)
        {
            return DownloadSendResult.Refused(
                DownloadSendStatus.ClientUnusable, $"“{client.Name}” is switched off. The owner can switch it on under Integrations.");
        }

        if (adapters.Find<IDownloadClient>(client.AdapterType) is not { } downloadClient)
        {
            return DownloadSendResult.Refused(
                DownloadSendStatus.ClientUnusable, $"This version of the app can't send downloads to “{client.AdapterType}”.");
        }

        return Takes(downloadClient.Capabilities, kind)
            ? null
            : DownloadSendResult.Refused(DownloadSendStatus.Invalid, $"“{client.Name}” can't take {KindsOf(kind)}, which this release is.");
    }

    /// <summary>Why the release can't be fetched from <paramref name="source"/>, or <c>null</c> when it can.
    /// ADR 0007 — a removed integration's releases fetch nothing: the key their links need went with it.</summary>
    private DownloadSendResult? SourceRefusal(IntegrationConnectionEntity? source)
    {
        if (source is null)
            return DownloadSendResult.Refused(DownloadSendStatus.SourceUnusable, SourceRemovedError);
        if (!source.IsEnabled)
        {
            return DownloadSendResult.Refused(
                DownloadSendStatus.SourceUnusable,
                $"“{source.Name}” is switched off, so its releases can't be fetched. The owner can switch it on under Integrations.");
        }

        return adapters.Find<IReleaseSearchProvider>(source.AdapterType) is null
            ? DownloadSendResult.Refused(
                DownloadSendStatus.SourceUnusable, $"This version of the app can't fetch releases from “{source.AdapterType}”.")
            : null;
    }

    private static string KindsOf(ReleasePayloadKind? kind) => kind switch
    {
        ReleasePayloadKind.TorrentFile => ".torrent files",
        ReleasePayloadKind.Magnet => "magnet links",
        ReleasePayloadKind.Nzb => "NZB files",
        _ => "torrents",
    };

    /// <summary>
    /// Decision 3 — whether a job that didn't fail already sent this release to the client: one whose
    /// release has the same source and the source's id for it, or the info hash this listing states (on
    /// the listing, or worked out from the payload that job sent). Any user's job: the client is the
    /// household's. A failed job sent nothing new, so it doesn't count. MAM-14 — nor does a cancelled one:
    /// cancelling is how a person sends a release again (MAM-14's decision 3), and while the torrent is still in
    /// the client, the client's own "already has this torrent" check (decision 4) stops a second copy. A paused
    /// one counts: it is still there and still followed.
    /// </summary>
    private Task<bool> WasSentAsync(ReleaseEntity release, Guid clientId, CancellationToken cancellationToken)
    {
        var sourceId = release.SourceConnectionId;
        var sourceItemId = release.SourceItemId;
        var infoHash = release.InfoHash;
        return db.DownloadJobs
            .AsNoTracking()
            .Where(job => job.DownloadClientConnectionId == clientId
                && job.Status != DownloadJobStatus.Failed
                && job.Status != DownloadJobStatus.Cancelled)
            .Join(db.Releases, job => job.ReleaseId, sent => sent.Id, (job, sent) => new { Job = job, Sent = sent })
            // Each side only when the listing has it: two listings without an id, or without a hash,
            // are not the same release for that.
            .AnyAsync(
                row => (sourceItemId != null && row.Sent.SourceConnectionId == sourceId && row.Sent.SourceItemId == sourceItemId)
                    || (infoHash != null && (row.Sent.InfoHash == infoHash || row.Job.InfoHashV1 == infoHash || row.Job.InfoHashV2 == infoHash)),
                cancellationToken);
    }

    // ── reads ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The release, when it is one of <paramref name="callerUserId"/>'s searches' from the last day — the
    /// lookup <c>SearchService</c> makes, through the release's search source. A release a plain release
    /// search found (MAM-10) belongs to no search, and one whose search was removed to none any more.
    /// </summary>
    private Task<ReleaseEntity?> FindReleaseAsync(Guid releaseId, Guid callerUserId, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().UtcDateTime - ReleaseStore.UnusedReleaseLifetime;
        return (
            from release in db.Releases.AsNoTracking()
            join searchSource in db.SearchSources.AsNoTracking() on release.SearchSourceId equals (Guid?)searchSource.Id
            join search in db.Searches.AsNoTracking() on searchSource.SearchId equals search.Id
            where release.Id == releaseId && search.UserId == callerUserId && search.CreatedUtc >= cutoff
            select release).SingleOrDefaultAsync(cancellationToken);
    }

    // Not a download client is the same as none (the DestinationService rule). Read with its path mappings,
    // which the category's confirmation maps the client's folders through.
    private Task<IntegrationConnectionEntity?> FindDownloadClientAsync(Guid downloadClientId, CancellationToken cancellationToken) =>
        db.IntegrationConnections
            .AsNoTracking()
            .Include(client => client.PathMappings)
            .SingleOrDefaultAsync(client => client.Id == downloadClientId && client.Kind == IntegrationKind.DownloadClient, cancellationToken);

    /// <summary>The job <paramref name="callerUserId"/>'s request <paramref name="requestId"/> made, with its
    /// release's title and source.</summary>
    private Task<JobRow?> FindRequestAsync(Guid callerUserId, Guid requestId, CancellationToken cancellationToken) =>
        JobRows(job => job.RequestedByUserId == callerUserId && job.RequestId == requestId).SingleOrDefaultAsync(cancellationToken);

    private async Task<DownloadJobDto?> FindJobAsync(Guid jobId, bool forOwner, CancellationToken cancellationToken) =>
        await JobRows(job => job.Id == jobId).SingleOrDefaultAsync(cancellationToken) is { } row ? ToDto(row, forOwner) : null;

    /// <summary>
    /// The jobs <paramref name="which"/> picks, each with its release's title and source. Filtered before the
    /// join: EF Core can't see through the <see cref="JobRow"/> it projects into, so a filter on the row after
    /// it fails to translate. The release can't be gone while a job points at it (Restrict), so the inner join
    /// loses no job.
    /// </summary>
    private IQueryable<JobRow> JobRows(Expression<Func<DownloadJobEntity, bool>> which) =>
        db.DownloadJobs
            .AsNoTracking()
            .Where(which)
            .Join(
                db.Releases.AsNoTracking(),
                job => job.ReleaseId,
                release => release.Id,
                (job, release) => new JobRow(job, release.Title, release.SourceName));

    /// <summary>
    /// The answer to a request id sent again: its job when it asks for what the job was made for, a refusal
    /// when it asks for something else. A client or destination removed since reads as the one asked for:
    /// the job no longer names it, and the retry is still the same request.
    /// </summary>
    private DownloadSendResult Replay(JobRow earlier, SendDownloadRequest request, bool forOwner)
    {
        var job = earlier.Job;
        var same = job.ReleaseId == request.ReleaseId
            && (job.DownloadClientConnectionId is null || job.DownloadClientConnectionId == request.DownloadClientId)
            && (job.DestinationMappingId is null || job.DestinationMappingId == request.DestinationId);
        return same
            ? DownloadSendResult.Replayed(ToDto(earlier, forOwner))
            : DownloadSendResult.Refused(DownloadSendStatus.RequestIdReused, RequestIdReusedError);
    }

    private DownloadJobDto ToDto(JobRow row, bool forOwner) => mapper.ToDto(row.Job, row.ReleaseTitle, row.SourceName ?? "", forOwner);

    /// <summary>MAM-14 — why a job of <paramref name="status"/>, which ended, can't be cancelled.</summary>
    private static string NothingToStopError(DownloadJobStatus status) => status switch
    {
        DownloadJobStatus.Downloaded => AlreadyDownloadedError,
        DownloadJobStatus.Failed => AlreadyFailedError,
        _ => AlreadyCancelledError,
    };

    // ── writes ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the job, still <see cref="DownloadJobStatus.Created"/>, with its audit row, in one save: the
    /// row a retry finds, written before anything is fetched or sent (specification §3.2, step 5). The
    /// job's id, or the answer to a save the database refused because another request got there first.
    /// </summary>
    private async Task<(Guid Id, DownloadSendResult? Refusal)> CreateJobAsync(
        Guid requestId,
        Guid callerUserId,
        bool forOwner,
        ReleaseEntity release,
        IntegrationConnectionEntity client,
        DestinationMappingEntity destination,
        CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(callerUserId, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var job = new DownloadJobEntity
        {
            RequestId = requestId,
            RequestedByUserId = actor.UserId,
            RequestedByUserName = actor.UserName,
            ReleaseId = release.Id,
            DownloadClientConnectionId = client.Id,
            DownloadClientName = client.Name,
            DestinationMappingId = destination.Id,
            DestinationName = destination.Name,
            // As the client spells it, never trimmed: it is what the submission sends (DestinationRules).
            ClientDestinationKey = destination.ClientDestinationKey,
            Status = DownloadJobStatus.Created,
            StatusChangedUtc = now,
            CreatedUtc = now,
        };
        db.DownloadJobs.Add(job);
        // One row per request, whatever the sending makes of it: who asked for which release to go where.
        auditTrail.Record(AuditAction.DownloadRequested, actor, AuditTarget.DownloadJob(job, release.Title));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (await LostRaceAsync(exception, requestId, callerUserId, forOwner, release, client, destination, cancellationToken) is { } lost)
                return (Guid.Empty, lost);
            throw;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "{ActorUserName} sent release {ReleaseId} to integration {IntegrationId} ({AdapterType}) as download job {JobId}.",
            actor.UserName,
            release.Id,
            client.Id,
            client.AdapterType,
            job.Id);
        return (job.Id, null);
    }

    /// <summary>
    /// The answer to a job the database refused because another request got there first, or <c>null</c>
    /// when it was something else (a full disk), which stays a failure. The MAM-4 race: two copies of one
    /// request — a double click, a retry while the first is still sending — can both find no job, and the
    /// unique index lets only one commit; the loser answers the winner's job. The release pruned, or the
    /// client or destination removed, between the checks and the save break a foreign key instead.
    /// </summary>
    private async Task<DownloadSendResult?> LostRaceAsync(
        DbUpdateException exception,
        Guid requestId,
        Guid callerUserId,
        bool forOwner,
        ReleaseEntity release,
        IntegrationConnectionEntity client,
        DestinationMappingEntity destination,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        if (await FindRequestAsync(callerUserId, requestId, cancellationToken) is { } winner)
        {
            logger.LogInformation(
                exception, "A concurrent copy of request {RequestId} made download job {JobId} first; this one answered it.", requestId, winner.Job.Id);
            return Replay(winner, new SendDownloadRequest(requestId, release.Id, client.Id, destination.Id), forOwner);
        }

        if (!await db.Releases.AnyAsync(row => row.Id == release.Id, cancellationToken))
            return DownloadSendResult.Refused(DownloadSendStatus.ReleaseMissing, ReleaseMissingError);
        if (!await db.IntegrationConnections.AnyAsync(row => row.Id == client.Id, cancellationToken))
            return DownloadSendResult.Refused(DownloadSendStatus.ClientMissing, ClientGoneError);
        if (!await db.DestinationMappings.AnyAsync(row => row.Id == destination.Id, cancellationToken))
            return DownloadSendResult.Refused(DownloadSendStatus.DestinationMissing, DestinationGoneError);
        return null;
    }

    // Read from the database rather than the token: the job and the audit row carry the name the account has.
    private Task<AuditActor> ActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    /// <summary>A job with its release's title and source, read together.</summary>
    private sealed record JobRow(DownloadJobEntity Job, string ReleaseTitle, string? SourceName);
}
