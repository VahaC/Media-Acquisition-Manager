using System.Linq.Expressions;
using MediaAcquisitionManager.Api.Auth;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Destinations;
using MediaAcquisitionManager.Api.Services.Releases;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <inheritdoc cref="IDownloadService"/>
/// <remarks>
/// <para>
/// This service decides what may be sent and writes the job; <see cref="IDownloadSender"/> talks to the
/// source and the client. MAM-14 — it reads and cancels jobs without talking to anyone: what the client says
/// about a job is <see cref="IDownloadJobPoller"/>'s to read.
/// </para>
/// <para>
/// MAM-24 — a replacement of old copies is a send with more to it, never a second copy of the send: both routes run one
/// private core, which plans the replacement (<see cref="IReplacementPlanner"/>, the database and the disk alone) after
/// the send's own checks, writes it with the job in the job's one save, sends in strict mode, and brings the replacement
/// in line with how the send went (<see cref="IReplacementJobFollower"/>). Every job it answers carries its replacement.
/// </para>
/// </remarks>
public sealed class DownloadService(
    ApplicationDbContext db,
    IIntegrationAdapterRegistry adapters,
    IDownloadSender sender,
    IDestinationChecker destinations,
    IDownloadOverwriteGuard overwrites,
    IReplacementPlanner planner,
    IReplacementJobFollower replacements,
    IReplacementGates gates,
    IAuditTrail auditTrail,
    IDownloadMapper mapper,
    TimeProvider time,
    ILogger<DownloadService> logger) : IDownloadService
{
    private const string ClientGoneError = "The download client you picked no longer exists. Reload the page and pick one again.";

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

    // MAM-24 — why a replacement ends when its download is cancelled from the Jobs page (R6).
    private const string ReplacementCancelledReason = "You cancelled the download, so nothing will be replaced.";

    /// <summary>
    /// MAM-26 — the states of a name that moved into the quarantine at some point: in it, on its way back or out, back, or
    /// deleted. A job holding one moved something, so the companions that stayed are worth a word on its line.
    /// </summary>
    private static readonly IReadOnlySet<ReplacementNameState> MovedStates = new HashSet<ReplacementNameState>
    {
        ReplacementNameState.Quarantined,
        ReplacementNameState.Restoring,
        ReplacementNameState.Restored,
        ReplacementNameState.Deleting,
        ReplacementNameState.Deleted,
    };

    public async Task<DownloadOptionsResult> GetOptionsAsync(string? releaseId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        Guid? id = Guid.TryParse(releaseId, out var parsed) ? parsed : null;
        if (DownloadRules.ValidateReleaseId(id) is { } error)
            return DownloadOptionsResult.Invalid(error);
        if (await FindReleaseAsync(id!.Value, callerUserId, cancellationToken) is not { } release)
            return DownloadOptionsResult.ReleaseMissing(DownloadErrors.ReleaseMissing);

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
                options.Add(mapper.ToOption(client, adapter.Descriptor.DisplayName, own, downloadClient.Capabilities.DestinationKind));
        }

        var offered = options.SelectMany(option => option.Destinations).ToList();
        var suggested = DestinationSuggestion.For(
            await SectionsHoldingAsync(release, cancellationToken),
            offered.Select(destination => destination.Id).ToHashSet(),
            offered.FirstOrDefault(destination => destination.IsDefault)?.Id);
        return DownloadOptionsResult.Listed(new DownloadOptionsResponse(options, suggested));
    }

    public async Task<DownloadOverwriteCheckResult> GetOverwriteCheckAsync(
        string? destinationId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        Guid? id = Guid.TryParse(destinationId, out var parsed) ? parsed : null;
        if (DownloadRules.ValidateDestinationId(id) is { } error)
            return DownloadOverwriteCheckResult.Invalid(error);
        if (await db.DestinationMappings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, cancellationToken) is not { } destination)
            return DownloadOverwriteCheckResult.DestinationMissing(DownloadErrors.DestinationGone);

        // MAM-38 — through the Destinations' own listing, as the send reads it: a client that is switched off or
        // can't be asked leaves the folder unknown, never a guess. MAM-30 — a folder client is proposed this destination's
        // folder and finds it by folder; one it can't see is unknown too (D5).
        // MAM-31 — and why it is unknown, so the dialog's warning names the client and the fix: the live check stopped
        // Transmission and read "has no path in the app, or its download client can't say where it is" instead.
        CheckedClientDestination? listed = null;
        DownloadFolderUnknownCause? cause = null;
        string? clientError = null;
        var client = await FindDownloadClientAsync(destination.DownloadClientConnectionId, cancellationToken);
        if (client is not null)
        {
            var listing = await destinations.ListAsync(client, [destination.ClientDestinationKey], cancellationToken);
            if (listing.Status != ClientDestinationsStatus.Listed)
                (cause, clientError) = (DownloadFolderUnknownCause.ClientNotAsked, listing.Error);
            else if ((listed = listing.Find(destination.ClientDestinationKey)) is null)
                cause = DownloadFolderUnknownCause.CategoryMissing;
            // A folder the client names that none of its mappings covers. One it doesn't name at all, two mappings
            // sending it to different places, or a folder that isn't a full path has no mapping to add: the warning
            // says "add one" only when that is the fix.
            else if (listed.Check is { Status: DestinationPathStatus.PathUnknown, ServicePath: { } servicePath }
                && PathMapper.Map(servicePath, [.. client.PathMappings.Select(mapping => new PathMappingPair(mapping.ServicePrefix, mapping.AppPrefix))])
                    .Status == PathMapStatus.NotCovered)
                cause = DownloadFolderUnknownCause.NoPathMapping;
        }

        var reason = await overwrites.FolderReasonAsync(listed, cancellationToken);
        if (reason != DownloadOverwriteCheck.NotCheckedFolderUnknown || cause is null)
            return DownloadOverwriteCheckResult.Answered(new DownloadOverwriteCheckResponse(reason));

        // The client's own sentence can name its address: the owner's alone (MAM-10's rule), as on the Jobs page.
        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        return DownloadOverwriteCheckResult.Answered(new DownloadOverwriteCheckResponse(
            reason, cause, client!.Name, forOwner ? clientError : null));
    }

    public Task<DownloadSendResult> SendAsync(SendDownloadRequest request, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendAsync(request, callerUserId, replacement: null, cancellationToken);
    }

    // The lookup the send makes first, so a retry is recognised by the one rule.
    public async Task<bool> IsRetryAsync(Guid requestId, Guid callerUserId, CancellationToken cancellationToken = default) =>
        await FindRequestAsync(callerUserId, requestId, cancellationToken) is not null;

    public Task<DownloadSendResult> ScheduleReplacementAsync(
        ScheduleReplacementRequest request, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A missing list is an empty one, which the rules refuse (fail closed): nothing is ticked by default.
        return SendAsync(
            new SendDownloadRequest(request.RequestId, request.ReleaseId, request.DownloadClientId, request.DestinationId),
            callerUserId,
            new ReplacementRequest(request.FileIds ?? [], request.ConfirmedWorkIds ?? []),
            cancellationToken);
    }

    /// <summary>
    /// The one send both routes make: <paramref name="replacement"/> is <c>null</c> for a Keep both send, and otherwise
    /// the copies to replace, planned and written with the job and sent in strict mode (MAM-24).
    /// </summary>
    private async Task<DownloadSendResult> SendAsync(
        SendDownloadRequest request, Guid callerUserId, ReplacementRequest? replacement, CancellationToken cancellationToken)
    {
        // MAM-13 — checked in this order: the ids present; this very request sent before; the release the
        // caller's; the client and the destination there and belonging together; the client usable and
        // able to take the payload; the source still there to fetch from; the release not already sent to
        // the client. One sentence per answer, the first one broken, and nothing written or sent for any.
        // MAM-24 — and for a replacement, its ticks by their shape with the ids, and planned last, right before the job
        // is written.
        if (DownloadRules.Validate(request.RequestId, request.ReleaseId, request.DownloadClientId, request.DestinationId) is { } error)
            return DownloadSendResult.Refused(DownloadSendStatus.Invalid, error);
        if (replacement is not null && ReplacementRules.ValidateFiles(replacement.FileIds) is { } filesError)
            return DownloadSendResult.Refused(DownloadSendStatus.ReplacementInvalid, filesError);

        var requestId = request.RequestId!.Value;
        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        // First, before anything else is read: a retry is answered with the job its request made even after
        // the release, the client or the destination changed or went (ADR 0013).
        if (await FindRequestAsync(callerUserId, requestId, cancellationToken) is { } earlier)
            return await ReplayAsync(earlier, request, replacement, forOwner, cancellationToken);

        if (await CallerSearches.FindReleaseAsync(db, request.ReleaseId!.Value, callerUserId, time, cancellationToken) is not { } found)
            return DownloadSendResult.Refused(DownloadSendStatus.ReleaseMissing, DownloadErrors.ReleaseMissing);
        var release = found.Release;
        if (await FindDownloadClientAsync(request.DownloadClientId!.Value, cancellationToken) is not { } client)
            return DownloadSendResult.Refused(DownloadSendStatus.ClientMissing, ClientGoneError);
        if (await db.DestinationMappings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == request.DestinationId, cancellationToken)
            is not { } destination)
            return DownloadSendResult.Refused(DownloadSendStatus.DestinationMissing, DownloadErrors.DestinationGone);
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
                return await ReplayAsync(racing, request, replacement, forOwner, cancellationToken);

            // Decision 3 — the release is recognised by its source's id for it (or its info hash), not by
            // the row: every search stores its own copy of a listing (ADR 0006, MAM-10).
            return DownloadSendResult.Refused(
                DownloadSendStatus.AlreadySent, $"This release was already sent to “{client.Name}”, so it isn't sent twice.");
        }

        // MAM-24, R1 — the replacement planned from the database and the disk alone, the ticks read against the works and
        // copies as they are now: no client is asked before the job is written (ADR 0013). What only the client can say —
        // where the category saves, what the release turns out to be — is the strict send's, on a written job.
        ReplacementPlan? plan = null;
        if (replacement is not null)
        {
            var planned = await planner.PlanAsync(found, replacement, cancellationToken);
            if (planned.Plan is null)
            {
                // A copy of this very request may have written its job and replacement since the WasSent check above:
                // its copies then read as already scheduled — by that copy's own replacement. The copy is answered with
                // that job, as in the WasSent branch (ADR 0013), never refused as held by another download.
                if (await FindRequestAsync(callerUserId, requestId, cancellationToken) is { } racing)
                    return await ReplayAsync(racing, request, replacement, forOwner, cancellationToken);

                return DownloadSendResult.Refused(
                    planned.Status == ReplacementPlanStatus.Invalid ? DownloadSendStatus.ReplacementInvalid : DownloadSendStatus.ReplacementUnavailable,
                    planned.Error!);
            }

            plan = planned.Plan;
        }

        var job = await CreateJobAsync(requestId, callerUserId, forOwner, release, client, destination, replacement, plan, cancellationToken);
        if (job.Refusal is not null)
            return job.Refusal;

        // The job and its audit row are committed: from here the request is sent once, and its retries
        // answer this job.
        // MAM-30 — with the destination's name: a folder client's label, and what the app's sentences call it.
        await sender.SendAsync(job.Id, release, source!, client, destination.ClientDestinationKey, destination.Name, replacement: plan is not null);
        if (plan is not null)
            await FollowReplacementAsync(job.Id);

        var sent = await FindJobAsync(job.Id, forOwner, cancellationToken);
        return DownloadSendResult.Created(sent!);
    }

    // Another user's job answers as none, like their searches: nothing confirms it exists.
    public async Task<DownloadJobDto?> GetAsync(Guid jobId, Guid callerUserId, CancellationToken cancellationToken = default) =>
        await JobRows(job => job.Id == jobId && job.RequestedByUserId == callerUserId).SingleOrDefaultAsync(cancellationToken) is { } row
            ? await ToDtoAsync(row, await db.IsInstallationOwnerAsync(callerUserId, cancellationToken), cancellationToken)
            : null;

    public async Task<DownloadJobListResult> ListAsync(
        string? status, string? view, int page, int pageSize, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return DownloadJobListResult.Invalid(pagingError);
        if (DownloadJobListRules.Validate(status, out var filter) is { } statusError)
            return DownloadJobListResult.Invalid(statusError);
        if (DownloadJobListRules.ValidateView(view, out var shown) is { } viewError)
            return DownloadJobListResult.Invalid(viewError);

        // MAM-14's decision 1: the caller's own jobs — the owner's too, as GetAsync answers. A job nobody is
        // recorded as sending (its sender's account removed) is nobody's to list.
        var own = db.DownloadJobs.AsNoTracking().Where(job => job.RequestedByUserId == callerUserId);
        // MAM-41 — the gates read once, before anything else: the tab a job is counted in and the switch its line shows come
        // from the same read, and the page's lines reuse it rather than read it again (ADR 0033: fresh, never cached).
        var gate = await gates.ReadAsync(cancellationToken);
        var replacementJobs = db.ReplacementJobs.AsNoTracking();
        // The tabs' labels: the caller's own jobs in each, whatever this request filters and pages — a count each, in SQL.
        var needsYouCount = await own.Where(DownloadJobViews.Of(DownloadJobView.NeedsYou, replacementJobs, gate.IsEnabled)).CountAsync(cancellationToken);
        var inProgressCount = await own.Where(DownloadJobViews.Of(DownloadJobView.InProgress, replacementJobs, gate.IsEnabled)).CountAsync(cancellationToken);

        var jobs = own;
        if (filter is { } only)
            jobs = jobs.Where(job => job.Status == only);
        if (shown is { } tab)
            jobs = jobs.Where(DownloadJobViews.Of(tab, replacementJobs, gate.IsEnabled));

        var totalCount = await jobs.CountAsync(cancellationToken);
        // Past the last page there is nothing to read; and a page far enough out would overflow the offset,
        // which EF Core takes as an int (the UserAccountService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return DownloadJobListResult.Listed(new DownloadJobListResponse([], totalCount, page, pageSize, needsYouCount, inProgressCount));

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
        var rows = await JobRows(job => ids.Contains(job.Id)).ToListAsync(cancellationToken);
        var forOwner = await db.IsInstallationOwnerAsync(callerUserId, cancellationToken);
        var answered = (await ToDtosAsync(rows, forOwner, gate, cancellationToken)).ToDictionary(job => job.Id);
        return DownloadJobListResult.Listed(
            new DownloadJobListResponse([.. ids.Select(id => answered[id])], totalCount, page, pageSize, needsYouCount, inProgressCount));
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

    public async Task<DownloadCancelResult> CancelAsync(Guid jobId, Guid callerUserId, bool mayEndReplacement, CancellationToken cancellationToken = default)
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
            var now = time.GetUtcNow().UtcDateTime;
            if (await db.DownloadJobs.Where(job => job.RequestedByUserId == callerUserId).CancelAsync(jobId, now) == 1)
            {
                // MAM-5 — a personal access token never touches a replacement (ADR 0040), and this cancel, having taken
                // the job, would end the job's replacement too, below. Asked only once the job is taken, so a cancel that
                // would change nothing (a Downloaded job whose replacement waits in review, say) gets the answer a
                // session gets, not a refusal; and inside the transaction, which holds SQLite's write lock, so an owner's
                // Retry can't bring an ended replacement back between the question and the cancel. Leaving the block
                // without a commit rolls the job's cancel back: nothing changes, and nothing is recorded.
                if (!mayEndReplacement
                    && row.Replacement is { } followedReplacement
                    && await db.ReplacementJobs.AnyAsync(
                        replacement => replacement.Id == followedReplacement.Id && EF.Constant(ReplacementJobStates.Followed).Contains(replacement.State),
                        cancellationToken))
                {
                    return DownloadCancelResult.ReplacementNeedsTheWebApp;
                }

                var actor = await ActorAsync(callerUserId, cancellationToken);
                auditTrail.Record(AuditAction.DownloadCancelled, actor, AuditTarget.DownloadJob(row.Job, row.ReleaseTitle));
                // MAM-24, R6 — its replacement ends with it, in the same transaction and with a row of its own, from any
                // state still followed — a review included, which nothing else would end. Conditional, as the follower's
                // writes are: one that ended meanwhile is left as it is.
                var replacementCancelled = row.Replacement is { } replacement
                    && await db.ReplacementJobs.CancelAsync(replacement.Id, ReplacementCancelledReason, now) == 1;
                if (replacementCancelled)
                    auditTrail.Record(AuditAction.ReplacementCancelled, actor, AuditTarget.ReplacementJob(row.Replacement!, row.ReleaseTitle));
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("{ActorUserName} stopped following download job {JobId}.", actor.UserName, jobId);
                if (replacementCancelled)
                {
                    logger.LogInformation(
                        "{ActorUserName} cancelled replacement job {ReplacementJobId} with download job {JobId}.",
                        actor.UserName,
                        row.Replacement!.Id,
                        jobId);
                }

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
    /// lookup <c>SearchService</c> makes, through the release's search source (<see cref="CallerSearches"/>, MAM-24's
    /// one copy of the rule). A release a plain release search found (MAM-10) belongs to no search, and one whose
    /// search was removed to none any more.
    /// </summary>
    private async Task<ReleaseEntity?> FindReleaseAsync(Guid releaseId, Guid callerUserId, CancellationToken cancellationToken) =>
        (await CallerSearches.FindReleaseAsync(db, releaseId, callerUserId, time, cancellationToken))?.Release;

    /// <summary>
    /// MAM-32 — the mapped sections of the release's source that hold it: the rows of the categories the
    /// release was stored with (ADR 0020). None for a release of a removed source, or one stored before
    /// MAM-32, which kept no categories: the dialog picks the default for it, as it did.
    /// </summary>
    private async Task<IReadOnlyCollection<DestinationSectionEntity>> SectionsHoldingAsync(ReleaseEntity release, CancellationToken cancellationToken)
    {
        var categoryIds = release.CategoryIds?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (release.SourceConnectionId is not { } sourceId || categoryIds.Length == 0)
            return [];
        return await db.DestinationSections
            .AsNoTracking()
            .Where(section => section.ReleaseSourceConnectionId == sourceId && categoryIds.Contains(section.CategoryId))
            .ToListAsync(cancellationToken);
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
        await JobRows(job => job.Id == jobId).SingleOrDefaultAsync(cancellationToken) is { } row
            ? await ToDtoAsync(row, forOwner, cancellationToken)
            : null;

    /// <summary>
    /// The jobs <paramref name="which"/> picks, each with its release's title and source — and (MAM-24) the replacement
    /// it carries, if any, by a left join: one per job, by its unique index. Filtered before the joins: EF Core can't see
    /// through the <see cref="JobRow"/> it projects into, so a filter on the row after it fails to translate. The release
    /// can't be gone while a job points at it (Restrict), so the inner join loses no job.
    /// </summary>
    private IQueryable<JobRow> JobRows(Expression<Func<DownloadJobEntity, bool>> which) =>
        from job in db.DownloadJobs.AsNoTracking().Where(which)
        join release in db.Releases.AsNoTracking() on job.ReleaseId equals release.Id
        from replacement in db.ReplacementJobs.AsNoTracking().Where(replacement => replacement.DownloadJobId == job.Id).DefaultIfEmpty()
        select new JobRow(job, release.Title, release.SourceName, replacement);

    /// <summary>
    /// The answer to a request id sent again: its job when it asks for what the job was made for, a refusal
    /// when it asks for something else. A client or destination removed since reads as the one asked for:
    /// the job no longer names it, and the retry is still the same request.
    /// </summary>
    /// <remarks>
    /// MAM-24, R4 — what it was made for includes its replacement: a Keep both send is answered with a job that carries
    /// none, and a schedule with one whose copies and confirmations are the ones it ticks, as sets. A request id one route
    /// used is something else to the other. Every place a request id is answered goes through here — the first lookup,
    /// the copy that wrote its job after it, and the one that lost the save to it — so none can tell them apart.
    /// </remarks>
    private async Task<DownloadSendResult> ReplayAsync(
        JobRow earlier, SendDownloadRequest request, ReplacementRequest? replacement, bool forOwner, CancellationToken cancellationToken)
    {
        var job = earlier.Job;
        var same = job.ReleaseId == request.ReleaseId
            && (job.DownloadClientConnectionId is null || job.DownloadClientConnectionId == request.DownloadClientId)
            && (job.DestinationMappingId is null || job.DestinationMappingId == request.DestinationId)
            && await IsSameReplacementAsync(earlier.Replacement, replacement, cancellationToken);
        return same
            ? DownloadSendResult.Replayed(await ToDtoAsync(earlier, forOwner, cancellationToken))
            : DownloadSendResult.Refused(DownloadSendStatus.RequestIdReused, RequestIdReusedError);
    }

    /// <summary>MAM-24 — whether <paramref name="scheduled"/>, the replacement an earlier job carries, is the one
    /// <paramref name="asked"/> asks for: none for none, or the same copies and confirmations.</summary>
    private async Task<bool> IsSameReplacementAsync(ReplacementJobEntity? scheduled, ReplacementRequest? asked, CancellationToken cancellationToken)
    {
        if (scheduled is null || asked is null)
            return scheduled is null && asked is null;

        var fileIds = await db.ReplacementJobFiles.AsNoTracking()
            .Where(file => file.ReplacementJobId == scheduled.Id)
            .Select(file => file.PhysicalFileId)
            .ToListAsync(cancellationToken);
        var confirmedWorkIds = await db.ReplacementJobWorks.AsNoTracking()
            .Where(work => work.ReplacementJobId == scheduled.Id && work.ConfirmedByOwner)
            .Select(work => work.MediaWorkId)
            .ToListAsync(cancellationToken);
        return asked.IsSameAs(fileIds, confirmedWorkIds);
    }

    private async Task<DownloadJobDto> ToDtoAsync(JobRow row, bool forOwner, CancellationToken cancellationToken) =>
        (await ToDtosAsync([row], forOwner, gate: null, cancellationToken))[0];

    /// <summary>
    /// The jobs as the dialog and the Jobs page show them, in <paramref name="rows"/>' order, each with its replacement.
    /// MAM-24, R10 — a replacement's counts read in a batch, never per job: one query for the copies of every replacement
    /// among the rows and one read of the gates — and neither for rows that carry none, as a page of Keep both sends does.
    /// MAM-26 — and one query for the names their moves planned, for what the quarantine holds of each. MAM-27 — still those
    /// three: what the line says of drops, Keep both and a running check comes from the same rows and the replacement's own.
    /// MAM-41 — the list hands in the <paramref name="gate"/> it already read for its tabs, so a page reads it once.
    /// </summary>
    private async Task<IReadOnlyList<DownloadJobDto>> ToDtosAsync(
        IReadOnlyList<JobRow> rows, bool forOwner, ReplacementGateState? gate, CancellationToken cancellationToken)
    {
        List<Guid> replacementIds = [.. rows.Select(row => row.Replacement?.Id).OfType<Guid>()];
        if (replacementIds.Count == 0)
            return [.. rows.Select(row => ToDto(row, forOwner))];

        // One JSON parameter (EF.Parameter) rather than one per id, the house rule against SQLite's limit.
        var copies = (await db.ReplacementJobFiles.AsNoTracking()
                .Where(file => EF.Parameter(replacementIds).Contains(file.ReplacementJobId))
                .Select(file => new ReplacementCopyPath(
                    file.ReplacementJobId, file.Id, file.AppPath, file.DroppedUtc != null, file.OnlyDropSettles))
                .ToListAsync(cancellationToken))
            .ToLookup(file => file.ReplacementJobId);
        // MAM-26 — the same way: only the columns the counts need, never a path.
        var names = (await db.ReplacementJobNames.AsNoTracking()
                .Where(name => EF.Parameter(replacementIds).Contains(name.ReplacementJobId))
                .Select(name => new ReplacementNameCount(name.ReplacementJobId, name.ReplacementJobFileId, name.Kind, name.State))
                .ToListAsync(cancellationToken))
            .ToLookup(name => name.ReplacementJobId);
        // Read fresh, never cached (ADR 0033): what the page says about the switch and the ticks is what they are now.
        var current = gate ?? await gates.ReadAsync(cancellationToken);
        return
        [
            .. rows.Select(row => row.Replacement is { } replacement
                ? ToDto(row, forOwner) with { Replacement = ReplacementOf(replacement, [.. copies[replacement.Id]], [.. names[replacement.Id]], current) }
                : ToDto(row, forOwner)),
        ];
    }

    private DownloadJobDto ToDto(JobRow row, bool forOwner) => mapper.ToDto(row.Job, row.ReleaseTitle, row.SourceName ?? "", forOwner);

    /// <summary>
    /// MAM-24, R10 — <paramref name="replacement"/> as the Jobs page shows it: whether replacement is switched off now,
    /// and how many of its <paramref name="copies"/> no longer lie in a ticked folder — every library folder holding one
    /// ticked and inside the Media folders, as the plan required. A stored path that no longer parses lies in none: it
    /// can't move.
    /// </summary>
    /// <remarks>
    /// MAM-26 — of the copies still to move only: check 5 passes over a copy whose video is in the quarantine already or
    /// was deleted from it, so its folder's tick stops nothing, while any other copy outside a ticked folder stops the whole
    /// replacement for review. And, from its <paramref name="names"/>, how many ticked copies have their video in the
    /// quarantine, and how many companions stayed in their folder in a job that moved something — one whose video never
    /// moved left them all where they were, which its reason says.
    /// MAM-27 — a copy the owner dropped is out of the job: it stays where it is, so it counts neither among the copies it
    /// would replace nor outside a ticked folder, only as dropped; and whether any name left its folder, which Keep both
    /// can't undo.
    /// MAM-41 — and the two facts Restore and Delete for good are offered by, read as the job's page reads them: whether any
    /// name is in the quarantine, and whether a video was deleted for good.
    /// </remarks>
    private DownloadJobReplacementDto ReplacementOf(
        ReplacementJobEntity replacement, IReadOnlyList<ReplacementCopyPath> copies, IReadOnlyList<ReplacementNameCount> names, ReplacementGateState gate)
    {
        var moved = names
            .Where(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Quarantined or ReplacementNameState.Deleted })
            .Select(name => name.ReplacementJobFileId)
            .ToHashSet();
        var kept = copies.Where(copy => !copy.Dropped).ToList();
        var counts = new ReplacementLineCounts(
            kept.Count,
            kept.Count(copy => !moved.Contains(copy.Id)
                && (!NormalizedPath.TryParse(copy.AppPath, out var parsed, out _) || !gate.IsInTickedFolder(parsed))),
            names.Where(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Quarantined })
                .Select(name => name.ReplacementJobFileId)
                .Distinct()
                .Count(),
            names.Any(name => MovedStates.Contains(name.State))
                ? names.Count(name => name is { Kind: ReplacementNameKind.Companion, State: ReplacementNameState.Stayed })
                : 0,
            copies.Count - kept.Count,
            kept.Count(copy => copy.OnlyDropSettles),
            names.Any(name => ReplacementNameStates.Taken.Contains(name.State)),
            names.Any(name => name.State == ReplacementNameState.Quarantined),
            names.Any(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Deleted }));
        return mapper.ToDto(replacement, counts, !gate.IsEnabled);
    }

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
    /// MAM-24 — with <paramref name="plan"/>, the replacement it carries is written in the same save
    /// (<see cref="AddReplacement"/>): a job and its replacement exist together or not at all.
    /// </summary>
    private async Task<(Guid Id, DownloadSendResult? Refusal)> CreateJobAsync(
        Guid requestId,
        Guid callerUserId,
        bool forOwner,
        ReleaseEntity release,
        IntegrationConnectionEntity client,
        DestinationMappingEntity destination,
        ReplacementRequest? replacement,
        ReplacementPlan? plan,
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
            // MAM-30 — what that key is, as the client's type says now: the job reads right after the type is gone. The
            // client refusal above made sure this build registers the type.
            DestinationKind = adapters.Find<IDownloadClient>(client.AdapterType)?.Capabilities.DestinationKind,
            Status = DownloadJobStatus.Created,
            StatusChangedUtc = now,
            CreatedUtc = now,
        };
        db.DownloadJobs.Add(job);
        // One row per request, whatever the sending makes of it: who asked for which release to go where.
        auditTrail.Record(AuditAction.DownloadRequested, actor, AuditTarget.DownloadJob(job, release.Title));
        var scheduled = plan is null ? null : AddReplacement(job, plan, actor, release.Title, now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            if (await LostRaceAsync(exception, requestId, callerUserId, forOwner, release, client, destination, replacement, cancellationToken) is { } lost)
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
        if (scheduled is not null)
        {
            // The ids and how many, never a path or a title.
            logger.LogInformation(
                "{ActorUserName} scheduled replacement job {ReplacementJobId} of {CopyCount} copies of {WorkCount} works with download job {JobId}.",
                actor.UserName,
                scheduled.Id,
                plan!.Files.Count,
                plan.Works.Count,
                job.Id);
        }

        return (job.Id, null);
    }

    /// <summary>
    /// MAM-24 — adds the replacement <paramref name="plan"/> describes to <paramref name="job"/>'s save, unsaved: the
    /// replacement, still <see cref="ReplacementJobState.Scheduled"/>, with the edition the release claims; each ticked
    /// copy as the plan read it from the disk; each matched work with its ids then; and its audit rows — the schedule's,
    /// named by the release, and one per work the owner confirmed, named by the words he ticked (ADR 0032's consent).
    /// </summary>
    private ReplacementJobEntity AddReplacement(DownloadJobEntity job, ReplacementPlan plan, AuditActor actor, string releaseTitle, DateTime now)
    {
        var scheduled = new ReplacementJobEntity
        {
            DownloadJobId = job.Id,
            RequestedByUserId = actor.UserId,
            RequestedByUserName = actor.UserName,
            State = ReplacementJobState.Scheduled,
            StateChangedUtc = now,
            // By name, as the enum is stored everywhere: a value added later never shifts what was kept.
            ReleaseEditions = string.Join(',', plan.ReleaseEditions),
            CreatedUtc = now,
        };
        db.ReplacementJobs.Add(scheduled);
        db.ReplacementJobFiles.AddRange(plan.Files.Select((file, position) => new ReplacementJobFileEntity
        {
            ReplacementJobId = scheduled.Id,
            Position = position,
            PhysicalFileId = file.PhysicalFileId,
            MediaVersionId = file.MediaVersionId,
            MediaWorkId = file.MediaWorkId,
            MatchedWorkId = file.MatchedWorkId,
            SeasonNumber = file.SeasonNumber,
            EpisodeNumber = file.EpisodeNumber,
            ServicePath = file.ServicePath,
            AppPath = file.AppPath,
            DeviceId = file.DeviceId,
            Inode = file.Inode,
            SizeBytes = file.SizeBytes,
            ModifiedUtc = file.ModifiedUtc,
            LinkCount = file.LinkCount,
            EditionLabel = file.EditionLabel,
        }));
        foreach (var work in plan.Works)
        {
            var row = new ReplacementJobWorkEntity
            {
                ReplacementJobId = scheduled.Id,
                MediaWorkId = work.WorkId,
                Kind = work.Kind,
                Title = work.Title,
                Year = work.Year,
                Evidence = work.Evidence,
                ConfirmedByOwner = work.ConfirmedByOwner,
                ConfirmationText = work.ConfirmationText,
            };
            db.ReplacementJobWorks.Add(row);
            db.ReplacementJobWorkIdentifiers.AddRange(work.Identifiers.Select(identifier => new ReplacementJobWorkIdentifierEntity
            {
                ReplacementJobWorkId = row.Id,
                Scheme = identifier.Scheme,
                Value = identifier.Value,
                SetByOwner = identifier.SetByOwner,
            }));
        }

        auditTrail.Record(AuditAction.ReplacementScheduled, actor, AuditTarget.ReplacementJob(scheduled, releaseTitle));
        foreach (var work in plan.Works.Where(work => work.ConfirmedByOwner))
            auditTrail.Record(AuditAction.ReplacementWorkConfirmed, actor, AuditTarget.ReplacementJob(scheduled, work.ConfirmationText));
        return scheduled;
    }

    /// <summary>
    /// MAM-24, R4 — brings the replacement just sent in line with how its send went, so the answer already says it (a
    /// strict refusal: Failed). A bug here costs the answer that, never the request: the job is sent, and the next pass
    /// of the following catches up. Error, with the type and the stack, never the message. Not cancelled, as the send
    /// isn't: the state is written whether or not the browser is still there.
    /// </summary>
    private async Task FollowReplacementAsync(Guid jobId)
    {
        try
        {
            await replacements.SyncAsync(jobId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Following the replacement of download job {JobId} after its send threw {ExceptionType} {StackTrace}",
                jobId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
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
        ReplacementRequest? replacement,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        if (await FindRequestAsync(callerUserId, requestId, cancellationToken) is { } winner)
        {
            logger.LogInformation(
                exception, "A concurrent copy of request {RequestId} made download job {JobId} first; this one answered it.", requestId, winner.Job.Id);
            return await ReplayAsync(
                winner, new SendDownloadRequest(requestId, release.Id, client.Id, destination.Id), replacement, forOwner, cancellationToken);
        }

        if (!await db.Releases.AnyAsync(row => row.Id == release.Id, cancellationToken))
            return DownloadSendResult.Refused(DownloadSendStatus.ReleaseMissing, DownloadErrors.ReleaseMissing);
        if (!await db.IntegrationConnections.AnyAsync(row => row.Id == client.Id, cancellationToken))
            return DownloadSendResult.Refused(DownloadSendStatus.ClientMissing, ClientGoneError);
        if (!await db.DestinationMappings.AnyAsync(row => row.Id == destination.Id, cancellationToken))
            return DownloadSendResult.Refused(DownloadSendStatus.DestinationMissing, DownloadErrors.DestinationGone);
        return null;
    }

    // Read from the database rather than the token: the job and the audit row carry the name the account has.
    private Task<AuditActor> ActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    /// <summary>A job with its release's title and source, and (MAM-24) the replacement it carries, read together.</summary>
    private sealed record JobRow(DownloadJobEntity Job, string ReleaseTitle, string? SourceName, ReplacementJobEntity? Replacement);

    /// <summary>MAM-26 — one name a replacement's move planned, with only what the Jobs line counts of it.</summary>
    private sealed record ReplacementNameCount(Guid ReplacementJobId, Guid ReplacementJobFileId, ReplacementNameKind Kind, ReplacementNameState State);

    /// <summary>MAM-24 — one ticked copy of a replacement, with only what the Jobs line counts of it: where it is. MAM-27 —
    /// and whether the owner dropped it, and whether its problem is one only a drop settles.</summary>
    private sealed record ReplacementCopyPath(Guid ReplacementJobId, Guid Id, string AppPath, bool Dropped, bool OnlyDropSettles);
}
