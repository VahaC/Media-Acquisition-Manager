using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <inheritdoc cref="IReplacementJobActions"/>
/// <remarks>
/// <para>
/// Split by action: the details and Retry here; Restore (<c>.Restore.cs</c>) and Delete for good (<c>.Delete.cs</c>), each
/// with the checks it makes before anything is written; and the one write loop both run name by name
/// (<c>.Writes.cs</c>) — the claim, each name's intent and outcome, the end —, so the two can't record a write two ways.
/// </para>
/// <para>
/// The answer to a Retry, a Restore or a Delete for good is the download job with its replacement line, read the way the
/// Jobs page reads it (<see cref="IDownloadService.GetAsync"/>): the page shows what it would show after a reload.
/// </para>
/// <para>
/// Information: a replacement's id, its states and counts, never a path, a title or a sentence.
/// </para>
/// </remarks>
public sealed partial class ReplacementJobActions(
    ApplicationDbContext db,
    IReplacementGates gates,
    IFileInventory files,
    IFileQuarantine quarantine,
    ReplacementJobsInHand inHand,
    IReplacementLibraryNotice notice,
    IDownloadService downloads,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<ReplacementJobActions> logger) : IReplacementJobActions
{
    /// <summary>What a Delete for good body MVC can't read gets (<c>UnreadableBodyErrorAttribute</c>): what it must
    /// be.</summary>
    public const string UnreadableBodyError = "The body must be JSON with the files you confirmed, such as { \"names\": [\"<id>\"] }.";

    // MAM-26 — a Retry of a job the mover didn't stop: MAM-25's review (its Retry is MAM-27's) or any other state.
    private const string NothingToRetryError = "This replacement isn't waiting for review after a move, so there's nothing to retry here.";

    // MAM-26 — someone in this process — the mover, or another click of the owner's — is writing for the job right now.
    private const string BeingWorkedOnError = "This replacement is being worked on right now. Try again in a moment.";

    // MAM-26 — the job left the state the request claimed it in, between the claim and a later write: never written over.
    private const string ChangedMeanwhileError =
        "This replacement changed while the app was working on it, so it stopped. Reload the page to see where it stands.";

    public async Task<ReplacementDetailsDto?> GetAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return null;

        // Every row of the plan, a position being unique per job: what moved, what stayed and what came of each.
        var names = await db.ReplacementJobNames
            .AsNoTracking()
            .Where(name => name.ReplacementJobId == replacementJobId)
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken);
        // Read fresh, never cached (ADR 0033): the buttons say whether they can work now.
        var gate = await gates.ReadAsync(cancellationToken);
        var quarantined = names.Where(name => name.State == ReplacementNameState.Quarantined).ToList();
        var linkCounts = await LinkCountsNowAsync(quarantined, gate, cancellationToken);
        int? LinkCountOf(ReplacementJobNameEntity name) => linkCounts.TryGetValue(name.Id, out var now) ? now : name.LinkCount;

        var replacement = job.Replacement;
        return new ReplacementDetailsDto(
            replacement.Id,
            replacement.DownloadJobId,
            replacement.State,
            replacement.Reason,
            !gate.IsEnabled,
            ReplacementJobOffers.CanRetry(replacement.State, replacement.QuarantineCheckedUtc),
            ReplacementJobOffers.CanRestore(
                replacement.State, quarantined.Count > 0, names.Any(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Deleted })),
            ReplacementJobOffers.CanDeleteForGood(replacement.State, quarantined.Count > 0),
            quarantined.Sum(name => name.SizeBytes),
            // ADR 0031 — a file with another name keeps its data when this one goes, so it frees nothing; a count the
            // platform didn't give can't promise the room either.
            quarantined.Where(name => LinkCountOf(name) == 1).Sum(name => name.SizeBytes),
            [
                .. names.Select(name => new ReplacementNameDto(
                    name.Id, name.Kind, name.AppPath, name.QuarantinePath, name.SizeBytes, LinkCountOf(name) > 1, name.State, name.Problem)),
            ]);
    }

    /// <summary>
    /// ADR 0031 — how many names each of <paramref name="quarantined"/> has now, by its row's id, as the disk says it at its
    /// quarantine path when it is still the file the move put there; the count the move read is the fallback. A torrent
    /// removed with its files since the move leaves the quarantined name the last one, and then deleting it frees its room.
    /// </summary>
    private async Task<Dictionary<Guid, int>> LinkCountsNowAsync(
        IReadOnlyList<ReplacementJobNameEntity> quarantined, ReplacementGateState gate, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var name in quarantined)
        {
            if (RootsOf(name, gate) is { } roots
                && await files.DescribeAsync(name.QuarantinePath, roots, cancellationToken) is { Status: FileInventoryStatus.Found, Value: { } facts }
                && facts.DeviceId == name.DeviceId
                && facts.Inode == name.Inode
                && facts.LinkCount is { } linkCount)
                counts[name.Id] = linkCount;
        }

        return counts;
    }

    public async Task<ReplacementActionResult> RetryAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken = default)
    {
        if (await FindOwnedAsync(replacementJobId, callerUserId, cancellationToken) is not { } job)
            return ReplacementActionResult.NotFound;
        if (!ReplacementJobOffers.CanRetry(job.Replacement.State, job.Replacement.QuarantineCheckedUtc))
            return ReplacementActionResult.Refused(NothingToRetryError);

        var actor = await ActorAsync(callerUserId, cancellationToken);
        if (!inHand.TryTake(replacementJobId))
            return ReplacementActionResult.Refused(BeingWorkedOnError);
        try
        {
            // Nothing is written to the media: the job goes back to the mover's queue, whose next pass checks its copies
            // again — the switch, the ticks, the torrents — before anything moves. Conditional, and the audit row in the
            // same transaction: a job that moved on meanwhile is left as it is, with nothing recorded.
            await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
            var now = time.GetUtcNow().UtcDateTime;
            if (await db.ReplacementJobs.RetryAsync(replacementJobId, now) == 0)
                return ReplacementActionResult.Refused(NothingToRetryError);

            auditTrail.Record(AuditAction.ReplacementRetried, actor, TargetOf(job));
            await db.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
            LogMove(replacementJobId, ReplacementJobState.NeedsReview, ReplacementJobState.Verified);
        }
        finally
        {
            inHand.Release(replacementJobId);
        }

        return await AnswerAsync(job, callerUserId);
    }

    // ── reads ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The caller's replacement <paramref name="replacementJobId"/> with its release's title (the audit rows' name), or
    /// <c>null</c>: another user's answers as none, as their download jobs do (ADR 0014). The download and the release
    /// can't be gone while the job points at them (Restrict), so the joins lose no job.
    /// </summary>
    private Task<OwnedJob?> FindOwnedAsync(Guid replacementJobId, Guid callerUserId, CancellationToken cancellationToken) =>
        (from replacement in db.ReplacementJobs.AsNoTracking().Where(job => job.Id == replacementJobId && job.RequestedByUserId == callerUserId)
         join download in db.DownloadJobs.AsNoTracking() on replacement.DownloadJobId equals download.Id
         join release in db.Releases.AsNoTracking() on download.ReleaseId equals release.Id
         select new OwnedJob(replacement, release.Title))
        .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The names of <paramref name="replacementJobId"/> in the quarantine now, videos first, then companions,
    /// each in its plan's order: the order Restore and Delete for good check and write them in.</summary>
    private async Task<List<ReplacementJobNameEntity>> QuarantinedNamesAsync(Guid replacementJobId, CancellationToken cancellationToken) =>
        // Ordered here, not by SQL: the kind is stored by name, and "Companion" sorts before "Video".
        [
            .. (await db.ReplacementJobNames
                    .AsNoTracking()
                    .Where(name => name.ReplacementJobId == replacementJobId && name.State == ReplacementNameState.Quarantined)
                    .ToListAsync(cancellationToken))
                .OrderBy(name => name.Kind)
                .ThenBy(name => name.Position),
        ];

    // Read from the database rather than the token: the audit rows carry the name the account has.
    private Task<AuditActor> ActorAsync(Guid actorUserId, CancellationToken cancellationToken) =>
        db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => new AuditActor(user.Id, user.UserName))
            .SingleAsync(cancellationToken);

    /// <summary>
    /// The download job of <paramref name="job"/> as the Jobs page reads it now, for the caller. Read after every write
    /// is committed and whatever this context tracked is let go; never cancelled, as the writes weren't.
    /// </summary>
    private async Task<ReplacementActionResult> AnswerAsync(OwnedJob job, Guid callerUserId)
    {
        db.ChangeTracker.Clear();
        return await downloads.GetAsync(job.Replacement.DownloadJobId, callerUserId, CancellationToken.None) is { } answer
            ? ReplacementActionResult.Done(answer)
            : ReplacementActionResult.NotFound;
    }

    private static AuditTarget TargetOf(OwnedJob job) => AuditTarget.ReplacementJob(job.Replacement, job.ReleaseTitle);

    private void LogMove(Guid replacementJobId, ReplacementJobState from, ReplacementJobState to) =>
        logger.LogInformation("Replacement job {ReplacementJobId} went from {From} to {To}.", replacementJobId, from, to);

    /// <summary>One of the caller's replacements, as the request read it, with its release's title.</summary>
    private sealed record OwnedJob(ReplacementJobEntity Replacement, string ReleaseTitle);
}
