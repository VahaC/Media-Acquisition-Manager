using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <inheritdoc cref="ICopyRemovalLibraryNotice"/>
/// <remarks>
/// <para>
/// What it says and how it is sent are a replacement's notice's own (<see cref="QuarantineNotices"/>): the video by the path
/// its library reported for the removed file, a companion by each holding library's own path of it; one request per
/// library and change. When it is settled is a replacement's notice's rule too: once every library took it, or when there
/// is nothing to send; at once when a library can never be told; and once <see cref="ReplacementVerificationRules.LibraryWait"/>
/// from when it fell due ran out, with a Warning.
/// </para>
/// <para>
/// Information: the removal's id and counts, never a path, a library's name or a sentence.
/// </para>
/// </remarks>
public sealed class CopyRemovalLibraryNotice(
    ApplicationDbContext db,
    IReplacementLibraryReader library,
    TimeProvider time,
    ILogger<CopyRemovalLibraryNotice> logger) : ICopyRemovalLibraryNotice
{
    public async Task SendDueAsync(Guid copyRemovalId, CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(copyRemovalId, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            // The reader never throws on a library's failure; this is a bug or the database failing. It costs this attempt
            // only — the notice stays owed for the next pass. Error, with the type and the stack, never the message.
            logger.LogError(
                "The library notice of copy removal {CopyRemovalId} threw {ExceptionType} {StackTrace}",
                copyRemovalId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
    }

    private async Task SendAsync(Guid copyRemovalId, CancellationToken cancellationToken)
    {
        var removal = await db.CopyRemovals
            .AsNoTracking()
            .Where(row => row.Id == copyRemovalId)
            .Select(row => new { row.LibraryNoticeDueUtc, row.ServicePath })
            .SingleOrDefaultAsync(cancellationToken);
        if (removal is not { LibraryNoticeDueUtc: { } dueUtc })
            return;

        var names = await db.CopyRemovalNames
            .AsNoTracking()
            .Where(name => name.CopyRemovalId == copyRemovalId
                && (name.State == ReplacementNameState.Quarantined || name.State == ReplacementNameState.Restored))
            .OrderBy(name => name.Position)
            .ToListAsync(cancellationToken);
        var folders = names.Count == 0 ? [] : (await LibraryFolderReader.LoadAsync(db, cancellationToken)).Mapped;
        // The removed file's video by the path its library reported, which its item is found by.
        var (notices, unplaced) = QuarantineNotices.Of(names, _ => removal.ServicePath, folders);
        if (unplaced > 0)
            logger.LogInformation(
                "The library notice of copy removal {CopyRemovalId} leaves out {UnplacedCount} names no library folder holds.", copyRemovalId, unplaced);
        var (waiting, givenUp) = await QuarantineNotices.TellAsync(library, notices, cancellationToken);

        // Reckoned after the requests, which can take a library's timeout each.
        var now = time.GetUtcNow().UtcDateTime;
        if (waiting && !ReplacementVerificationRules.LibraryWaitPassed(dueUtc, now))
        {
            logger.LogInformation("The library notice of copy removal {CopyRemovalId} stays owed: a library couldn't be told now.", copyRemovalId);
            return;
        }

        var settled = await db.CopyRemovals.SettleLibraryNoticeAsync(copyRemovalId, dueUtc);
        if (waiting)
            logger.LogWarning(
                "The library notice of copy removal {CopyRemovalId} was given up: a library couldn't be told within {Wait} of it falling due.",
                copyRemovalId,
                ReplacementVerificationRules.LibraryWait);
        else if (givenUp)
            logger.LogInformation("The library notice of copy removal {CopyRemovalId} was given up: a library can never be told.", copyRemovalId);
        else
            logger.LogInformation(
                "The library notice of copy removal {CopyRemovalId} was taken: {PathCount} paths in {RequestCount} requests; {SettledCount} settled.",
                copyRemovalId,
                notices.Sum(notice => notice.Value.Count),
                notices.Count,
                settled);
    }
}
