using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — writes the links "Accept all confident" queued into their libraries in the background (Queue/Worker,
/// decision 6), one at a time, each with its own audit row and its own result on the link, which the page polls.
/// </summary>
/// <remarks>
/// <para>
/// R13 — the rows are the queue: woken by <see cref="LibraryLinkWriteQueue"/>, the worker claims the oldest
/// <c>Queued</c> link (by when it was queued, then its id) until none is left, so a link queued while it works is
/// written in the same pass, and one a restart left queued is written after the reconciler's signal.
/// </para>
/// <para>
/// One scope and one try/catch per link — the next link's read and claim inside it too —, the other workers' rules: a
/// bug that throws out of one write leaves that link not written — logged — and the next still goes; a database that
/// fails before a link is claimed ends the pass — logged — and leaves the links queued, never stopping the app. One at a
/// time: the library is the owner's server, and a hundred writes at once would be a hundred read-post-read round trips
/// against it together.
/// </para>
/// </remarks>
public sealed class LibraryLinkWriteWorker(
    LibraryLinkWriteQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<LibraryLinkWriteWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
            await RunQueuedAsync(stoppingToken);
    }

    /// <summary>
    /// Writes every link queued right now, oldest first, and returns once none is left. The loop above calls it; tests
    /// drive it directly, as the test host runs no hosted services.
    /// </summary>
    /// <remarks>
    /// A shutdown stops it between two links, never inside one: a claimed link's write runs to its end
    /// (<see cref="CancellationToken.None"/>, ADR 0013), and the links still queued wait for the next start.
    /// </remarks>
    public async Task RunQueuedAsync(CancellationToken stoppingToken)
    {
        // The signals waiting are all answered by this pass.
        while (queue.Reader.TryRead(out _))
        {
        }

        var settled = new Dictionary<LibraryLinkWriteStatus, int>();
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid? claimedId = null;
            var attemptId = Guid.NewGuid();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var next = await db.LibraryItemLinks
                    .AsNoTracking()
                    .Where(link => link.WriteState == LibraryLinkWriteState.Queued)
                    .OrderBy(link => link.WriteStateUtc)
                    .ThenBy(link => link.Id)
                    .Select(link => new { link.Id, link.SetByUserId, link.SetByUserName })
                    .FirstOrDefaultAsync(stoppingToken);
                if (next is null)
                    break;

                // R9 — the claim: only the worker that moves the link out of Queued writes it; a link the owner replaced
                // or removed meanwhile is no longer Queued and is passed over.
                var claimed = await db.LibraryItemLinks
                    .Where(link => link.Id == next.Id && link.WriteState == LibraryLinkWriteState.Queued)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(link => link.WriteState, LibraryLinkWriteState.Writing)
                            .SetProperty(link => link.WriteAttemptId, attemptId)
                            .SetProperty(link => link.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                        stoppingToken) == 1;
                if (!claimed)
                    continue;
                claimedId = next.Id;

                // R13 — the audit rows of a background write name whoever queued it.
                var actor = next.SetByUserId is { } userId ? new AuditActor(userId, next.SetByUserName) : null;
                var result = await scope.ServiceProvider.GetRequiredService<ILibraryLinkWriter>().WriteAsync(next.Id, attemptId, actor, CancellationToken.None);
                Count(settled, result.Status);
            }
            // A shutdown is not an error: the links still queued wait for the next start. Anything else — a cancellation
            // too, when it isn't the shutdown's — is a bug or a database that failed, never a reason to stop the app.
            catch (Exception exception) when (!(exception is OperationCanceledException && stoppingToken.IsCancellationRequested))
            {
                if (claimedId is not { } linkId)
                {
                    // The next link couldn't be read or claimed (a lock held past its timeout by a long sync): the links
                    // stay queued for the next signal, or the reconciler's at the next start.
                    logger.LogError(exception, "The background writer couldn't take the next queued TMDb link.");
                    break;
                }

                logger.LogError(exception, "TMDb link {LinkId} threw out of the background writer.", linkId);
                await MarkNotWrittenAsync(linkId, attemptId);
                Count(settled, LibraryLinkWriteStatus.NotWritten);
            }
        }

        // Information: counts only — where each link's write left it, so a pass that wrote nothing doesn't read as done.
        var total = settled.Values.Sum();
        if (total > 0)
        {
            logger.LogInformation(
                "The background writer settled {Total} queued TMDb links: {Written} written, {NotWritten} not written, {Other} kept in the app or taken over meanwhile.",
                total,
                settled.GetValueOrDefault(LibraryLinkWriteStatus.Written),
                settled.GetValueOrDefault(LibraryLinkWriteStatus.NotWritten),
                settled.GetValueOrDefault(LibraryLinkWriteStatus.KeptInApp) + settled.GetValueOrDefault(LibraryLinkWriteStatus.ClaimLost));
        }
    }

    private static void Count(Dictionary<LibraryLinkWriteStatus, int> settled, LibraryLinkWriteStatus status) =>
        settled[status] = settled.GetValueOrDefault(status) + 1;

    // Best effort, in a fresh scope: the link's own context may be what broke. If even this fails, the reconciler turns
    // the link from Writing into NotWritten at the next start.
    private async Task MarkNotWrittenAsync(Guid linkId, Guid attemptId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LibraryItemLinks
                .Where(link => link.Id == linkId && link.WriteState == LibraryLinkWriteState.Writing && link.WriteAttemptId == attemptId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(link => link.WriteState, LibraryLinkWriteState.NotWritten)
                        .SetProperty(link => link.WriteError, FileInspectionRunUpdates.Cut(LibraryLinkWriter.AppErrorMessage, LibraryItemLinkEntity.ErrorMaximumLength))
                        .SetProperty(link => link.WriteStateUtc, time.GetUtcNow().UtcDateTime),
                    CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "TMDb link {LinkId} could not be marked not written.", linkId);
        }
    }
}
