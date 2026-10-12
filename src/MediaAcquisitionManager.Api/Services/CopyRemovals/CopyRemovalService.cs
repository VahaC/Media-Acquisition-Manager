using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Api.Mapping;
using MediaAcquisitionManager.Api.Services.Audit;
using MediaAcquisitionManager.Api.Services.Duplicates;
using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paging;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <inheritdoc cref="ICopyRemovalService"/>
/// <remarks>
/// <para>
/// Split by step: the list and the reads here; Remove's checks (<c>.Remove.cs</c>) and its claim, renames and end
/// (<c>.Moves.cs</c>); Restore (<c>.Restore.cs</c>) and Delete for good (<c>.Delete.cs</c>), which run the quarantine's one
/// write loop (<c>.Writes.cs</c>, <see cref="QuarantineWriteLoop{TName}"/>); the sentences (<c>.Reasons.cs</c>). Every
/// check and write a replacement's quarantine makes is shared with it, not copied: check 5's half
/// (<see cref="QuarantineCopyCheck"/>), the companions (<see cref="QuarantineCompanionListing"/>), check 6
/// (<see cref="QuarantineHoldingCheck"/>), the names' conditional writes (<c>ReplacementJobUpdates</c>), the owner's write
/// loop and its checks (<see cref="QuarantineNameChecks"/>), and the library's notice (<see cref="QuarantineNotices"/>).
/// </para>
/// <para>
/// The removals are the installation's, as the library and its quarantine are: the owner sees and acts on every one, not
/// only those he made, so a new owner can still put back what the old one removed.
/// </para>
/// <para>
/// Information: a removal's id, its states and counts, never a path, a title or a sentence.
/// </para>
/// </remarks>
public sealed partial class CopyRemovalService(
    ApplicationDbContext db,
    ILibraryDuplicateService duplicates,
    ICopyRemovalVerdicts verdicts,
    IReplacementGates gates,
    IReplacementHoldingReader holdingReader,
    ICopyRemovalLibraryNotice notice,
    ICopyRemovalMapper mapper,
    IFileInventory files,
    IFileQuarantine quarantine,
    CopyRemovalsInHand inHand,
    CopyRemovalTurns turns,
    IAuditTrail auditTrail,
    TimeProvider time,
    ILogger<CopyRemovalService> logger) : ICopyRemovalService
{
    public async Task<CopyRemovalListResult> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (PagingRules.Validate(page, pageSize) is { } pagingError)
            return CopyRemovalListResult.Invalid(pagingError);

        // Filtered by the states inlined as constants: they are IX_CopyRemovals_Open's own filter, which SQLite uses only for
        // a query naming them.
        var open = db.CopyRemovals.AsNoTracking().Where(removal => EF.Constant(CopyRemovalStates.Open).Contains(removal.State));
        var totalCount = await open.CountAsync(cancellationToken);
        var quarantinedBytes = await db.CopyRemovalNames
            .AsNoTracking()
            .Where(name => name.State == ReplacementNameState.Quarantined
                && open.Any(removal => removal.Id == name.CopyRemovalId))
            .SumAsync(name => (long?)name.SizeBytes, cancellationToken) ?? 0;
        // Read fresh, never cached (ADR 0033): the buttons say whether they can work now. Apart from each removal's offers,
        // as a replacement's page keeps its switchedOff: they say what the removal allows, the switch why it can't now.
        var gate = await gates.ReadAsync(cancellationToken);

        // Past the last page there is nothing to read; and a page far enough out would overflow the offset (the
        // LibraryService guard).
        var offset = (long)(page - 1) * pageSize;
        if (offset >= totalCount)
            return CopyRemovalListResult.Listed(new CopyRemovalListResponse([], totalCount, page, pageSize, quarantinedBytes, !gate.IsEnabled));

        // Newest first; the id breaks a tie, so a page reads the same way every time.
        var removals = await open
            .OrderByDescending(removal => removal.CreatedUtc)
            .ThenByDescending(removal => removal.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        List<Guid> ids = [.. removals.Select(removal => removal.Id)];
        // One read of the page's names, however many removals it holds (no N+1).
        var names = (await db.CopyRemovalNames
                .AsNoTracking()
                .Where(name => EF.Parameter(ids).Contains(name.CopyRemovalId))
                .ToListAsync(cancellationToken))
            .ToLookup(name => name.CopyRemovalId);
        // ADR 0031 — the page's quarantined names' link counts as the disk says them now.
        var linkCounts = await QuarantineNameChecks.LinkCountsNowAsync(
            files, removals.SelectMany(removal => names[removal.Id]).Where(name => name.State == ReplacementNameState.Quarantined), gate, cancellationToken);

        return CopyRemovalListResult.Listed(new CopyRemovalListResponse(
            [.. removals.Select(removal => mapper.ToDto(removal, [.. names[removal.Id]], linkCounts))],
            totalCount,
            page,
            pageSize,
            quarantinedBytes,
            !gate.IsEnabled));
    }

    // ── reads ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The removal <paramref name="copyRemovalId"/> as stored now, or <c>null</c>.</summary>
    private Task<CopyRemovalEntity?> FindAsync(Guid copyRemovalId, CancellationToken cancellationToken) =>
        db.CopyRemovals.AsNoTracking().SingleOrDefaultAsync(removal => removal.Id == copyRemovalId, cancellationToken);

    /// <summary>The names of <paramref name="copyRemovalId"/> in the quarantine now, the video first, then companions, each in
    /// its plan's order: the order Restore and Delete for good check and write them in.</summary>
    private async Task<List<CopyRemovalNameEntity>> QuarantinedNamesAsync(Guid copyRemovalId, CancellationToken cancellationToken) =>
        // Ordered here, not by SQL: the kind is stored by name, and "Companion" sorts before "Video".
        [
            .. (await db.CopyRemovalNames
                    .AsNoTracking()
                    .Where(name => name.CopyRemovalId == copyRemovalId && name.State == ReplacementNameState.Quarantined)
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
    /// The removal <paramref name="copyRemovalId"/> as the Removed copies list shows it now: read after every write is
    /// committed and whatever this context tracked is let go; never cancelled, as the writes weren't.
    /// </summary>
    private async Task<CopyRemovalResult> AnswerAsync(Guid copyRemovalId)
    {
        db.ChangeTracker.Clear();
        if (await FindAsync(copyRemovalId, CancellationToken.None) is not { } removal)
            return CopyRemovalResult.NotFound;

        var names = await db.CopyRemovalNames
            .AsNoTracking()
            .Where(name => name.CopyRemovalId == copyRemovalId)
            .ToListAsync(CancellationToken.None);
        var gate = await gates.ReadAsync(CancellationToken.None);
        var linkCounts = await QuarantineNameChecks.LinkCountsNowAsync(
            files, names.Where(name => name.State == ReplacementNameState.Quarantined), gate, CancellationToken.None);
        return CopyRemovalResult.Done(mapper.ToDto(removal, names, linkCounts));
    }

    /// <summary>
    /// ADR 0030 — tells the library the names left or came back, after the commit and outside any transaction: best effort.
    /// A notice that couldn't go stays owed (<c>LibraryNoticeDueUtc</c>) for the quarantine's next pass, and a bug here costs
    /// the notice, never the owner's answer: Error, with the type and the stack, never the message.
    /// </summary>
    private async Task TellLibraryAsync(Guid copyRemovalId)
    {
        try
        {
            await notice.SendDueAsync(copyRemovalId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Telling the library about copy removal {CopyRemovalId} threw {ExceptionType} {StackTrace}",
                copyRemovalId,
                exception.GetType().FullName,
                exception.StackTrace);
        }
    }

    private void LogMove(Guid copyRemovalId, CopyRemovalState? from, CopyRemovalState to) =>
        logger.LogInformation("Copy removal {CopyRemovalId} went from {From} to {To}.", copyRemovalId, from?.ToString() ?? "nothing", to);
}
