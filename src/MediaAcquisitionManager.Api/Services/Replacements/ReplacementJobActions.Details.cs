using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — the owner's read of a replacement for the job's page (ADR 0030, ADR 0033): every name of its move, where each
/// is now, the room the quarantine takes and which actions are offered. Read-only: it takes nothing in hand. MAM-27 — in a
/// file of its own, beside the actions; and with who stopped the job, how far the check of its new files got, every ticked
/// copy with its pair, where it stands and whether it may be dropped, the new files with their evidence, and the works they
/// are expected to be — the raw facts beside the statuses <see cref="ReplacementJobProgress"/> derives from them, so the
/// page never works a pair out again, and the offers by the rules the actions themselves apply
/// (<see cref="ReplacementJobOffers"/>).
/// </summary>
public sealed partial class ReplacementJobActions
{
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
        var linkCounts = await QuarantineNameChecks.LinkCountsNowAsync(files, quarantined, gate, cancellationToken);
        int? LinkCountOf(ReplacementJobNameEntity name) => linkCounts.TryGetValue(name.Id, out var now) ? now : name.LinkCount;

        // MAM-27 — one read per table, however many copies and new files the job holds (no N+1): a season pack's page is
        // read every 2 s while its check runs. The rows are the job's own and unpaged, as its names are.
        var copies = await db.ReplacementJobFiles
            .AsNoTracking()
            .Where(copy => copy.ReplacementJobId == replacementJobId)
            .OrderBy(copy => copy.Position)
            .ToListAsync(cancellationToken);
        var replacement = job.Replacement;
        var newFiles = await PageNewFilesAsync(replacementJobId, replacement.State, replacement.LibraryAskedUtc, cancellationToken);
        var works = await PageWorksAsync(replacementJobId, copies, cancellationToken);

        var stop = ReplacementJobProgress.StopOf(replacement.State, replacement.InspectionStartedUtc, replacement.QuarantineCheckedUtc);
        // A dropped copy's problem is history: it no longer withholds Retry.
        var dropOnlyCopyCount = copies.Count(copy => copy is { DroppedUtc: null, OnlyDropSettles: true });
        return new ReplacementDetailsDto(
            replacement.Id,
            replacement.DownloadJobId,
            replacement.State,
            texts.Render(replacement.Reason),
            !gate.IsEnabled,
            ReplacementJobOffers.CanRetry(stop, dropOnlyCopyCount),
            ReplacementJobOffers.CanRestore(
                replacement.State, quarantined.Count > 0, names.Any(name => name is { Kind: ReplacementNameKind.Video, State: ReplacementNameState.Deleted })),
            ReplacementJobOffers.CanDeleteForGood(replacement.State, quarantined.Count > 0),
            quarantined.Sum(name => name.SizeBytes),
            // ADR 0031 — a file with another name keeps its data when this one goes, so it frees nothing; a count the
            // platform didn't give can't promise the room either.
            quarantined.Where(name => LinkCountOf(name) == 1).Sum(name => name.SizeBytes),
            [
                .. names.Select(name => new ReplacementNameDto(
                    name.Id, name.Kind, name.AppPath, name.QuarantinePath, name.SizeBytes, LinkCountOf(name) > 1, name.State, texts.Render(name.Problem))),
            ])
        {
            CanCancel = ReplacementJobOffers.CanCancel(replacement.State, names.Any(name => ReplacementNameStates.Taken.Contains(name.State))),
            Stop = stop,
            CheckStage = ReplacementJobProgress.StageOf(replacement.State, replacement.LibraryAskedUtc),
            CheckStartedUtc = replacement.InspectionStartedUtc,
            LibraryAskedUtc = replacement.LibraryAskedUtc,
            CheckWaitsUntilUtc = ReplacementJobProgress.CheckWaitsUntilUtc(replacement.State, replacement.InspectionStartedUtc, replacement.LibraryAskedUtc),
            DropOnlyCopyCount = dropOnlyCopyCount,
            Copies = PageCopies(replacement.State, stop, copies, names, newFiles.Select(file => file.Id).ToHashSet()),
            NewFiles = newFiles,
            Works = works,
        };
    }

    // ── MAM-27: the copies, the new files and the works ────────────────────────────────────────

    /// <summary>
    /// MAM-27 — each of <paramref name="copies"/>, in order, of a job in <paramref name="state"/> stopped at
    /// <paramref name="stop"/>: where it stands, by its row, its video's name and the job's state, and whether the owner may
    /// drop it, by the names of it that left its folder and the copies left in the job.
    /// </summary>
    private List<ReplacementJobCopyDto> PageCopies(
        ReplacementJobState state,
        ReplacementStop? stop,
        IReadOnlyList<ReplacementJobFileEntity> copies,
        IReadOnlyList<ReplacementJobNameEntity> names,
        IReadOnlySet<Guid> newFileIds)
    {
        // A Retry's run plans a copy's names after the rows an earlier run left (a name lost in a cut, say): its video is
        // where the last of its video's names says.
        var videos = names
            .Where(name => name.Kind == ReplacementNameKind.Video)
            .GroupBy(name => name.ReplacementJobFileId)
            .ToDictionary(group => group.Key, group => group.MaxBy(name => name.Position)!.State);
        var taken = names.Where(name => ReplacementNameStates.Taken.Contains(name.State)).Select(name => name.ReplacementJobFileId).ToHashSet();
        var undroppedCopyCount = copies.Count(copy => copy.DroppedUtc is null);

        return
        [
            .. copies.Select(copy =>
            {
                // A dropped copy keeps the pair of the check it was dropped after, as history, and the next check of the
                // files deletes the new file it names: the page finds a pair among the new files, so one it can't find
                // there is sent as none.
                var pair = copy.PairedNewFileId is { } paired && newFileIds.Contains(paired) ? paired : (Guid?)null;
                var dropped = copy.DroppedUtc is not null;
                return new ReplacementJobCopyDto(
                    copy.Id,
                    copy.Position,
                    copy.AppPath,
                    copy.SizeBytes,
                    copy.MatchedWorkId,
                    copy.SeasonNumber,
                    copy.EpisodeNumber,
                    copy.EpisodeNumberEnd,
                    copy.EditionLabel,
                    copy.DurationMilliseconds,
                    pair,
                    texts.Render(copy.Problem),
                    // What settles a problem matters in a review: a pass that still waits keeps the mark beside the problem
                    // it found, and an ended job settles nothing more. A dropped copy keeps its last mark as history.
                    copy.OnlyDropSettles && (dropped || state == ReplacementJobState.NeedsReview),
                    copy.DroppedUtc,
                    // MAM-47 — the progress reads only whether there is a problem: its key stands for it.
                    ReplacementJobProgress.CopyStatusOf(
                        state, dropped, pair, copy.Problem?.Key, videos.TryGetValue(copy.Id, out var video) ? video : (ReplacementNameState?)null),
                    ReplacementJobOffers.CanDrop(state, stop, dropped, taken.Contains(copy.Id), undroppedCopyCount));
            }),
        ];
    }

    /// <summary>
    /// MAM-27 — the new files of <paramref name="replacementJobId"/> the last check listed, in the client's order, each with
    /// the ids the library gave it — its item's own, then its series' — and how far the check got with it, the job being in
    /// <paramref name="state"/> and its library having been asked at <paramref name="libraryAskedUtc"/>. Two reads, whatever
    /// the download holds.
    /// </summary>
    private async Task<List<ReplacementJobNewFileDto>> PageNewFilesAsync(
        Guid replacementJobId, ReplacementJobState state, DateTime? libraryAskedUtc, CancellationToken cancellationToken)
    {
        var newFiles = await db.ReplacementJobNewFiles
            .AsNoTracking()
            .Where(file => file.ReplacementJobId == replacementJobId)
            .OrderBy(file => file.Position)
            .ToListAsync(cancellationToken);
        // Ordered here, not by SQL: the scheme is stored by name.
        var identifiers = (await (
                    from identifier in db.ReplacementJobNewFileIdentifiers.AsNoTracking()
                    join file in db.ReplacementJobNewFiles on identifier.ReplacementJobNewFileId equals file.Id
                    where file.ReplacementJobId == replacementJobId
                    select identifier)
                .ToListAsync(cancellationToken))
            .OrderBy(identifier => identifier.OfSeries)
            .ThenBy(identifier => identifier.Scheme)
            .ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
            .ToLookup(identifier => identifier.ReplacementJobNewFileId);

        return
        [
            .. newFiles.Select(file => new ReplacementJobNewFileDto(
                file.Id,
                file.Position,
                file.RelativePath,
                file.AppPath,
                file.ClientSizeBytes,
                file.ClientProgress,
                file.SizeBytes,
                file.DurationMilliseconds,
                file.VideoCodec,
                file.Width,
                file.Height,
                file.CheckedUtc,
                file.Kind,
                file.SeasonNumber,
                file.EpisodeNumber,
                file.EpisodeNumberEnd,
                file.ReadUtc,
                file.MediaWorkId,
                texts.Render(file.Problem),
                ReplacementJobProgress.NewFileProgressOf(
                    state,
                    file.Problem?.Key,
                    file.ClientProgress,
                    file.CheckedUtc,
                    libraryAskedUtc,
                    file.ReadUtc,
                    file.LibraryItemId,
                    file.Kind,
                    file.Refreshed,
                    file.MediaWorkId,
                    file.SeasonNumber,
                    file.EpisodeNumber),
                [.. identifiers[file.Id].Select(identifier => new ReplacementJobIdentifierDto(identifier.Scheme, identifier.Value, identifier.OfSeries, false))])),
        ];
    }

    /// <summary>
    /// MAM-27 — the works <paramref name="replacementJobId"/> expects its new files to be, each with its registry ids as they
    /// were when it was scheduled. They carry no order of their own: each takes the place of its first copy among
    /// <paramref name="copies"/>, as the Download dialog listed a work with its copies. Two reads, however many there are.
    /// </summary>
    private async Task<List<ReplacementJobWorkDto>> PageWorksAsync(
        Guid replacementJobId, IReadOnlyList<ReplacementJobFileEntity> copies, CancellationToken cancellationToken)
    {
        var works = await db.ReplacementJobWorks
            .AsNoTracking()
            .Where(work => work.ReplacementJobId == replacementJobId)
            .ToListAsync(cancellationToken);
        // Ordered here, not by SQL: the scheme is stored by name.
        var identifiers = (await (
                    from identifier in db.ReplacementJobWorkIdentifiers.AsNoTracking()
                    join work in db.ReplacementJobWorks on identifier.ReplacementJobWorkId equals work.Id
                    where work.ReplacementJobId == replacementJobId
                    select identifier)
                .ToListAsync(cancellationToken))
            .OrderBy(identifier => identifier.Scheme)
            .ThenBy(identifier => identifier.Value, StringComparer.Ordinal)
            .ToLookup(identifier => identifier.ReplacementJobWorkId);
        var firstCopyOf = copies
            .GroupBy(copy => copy.MatchedWorkId)
            .ToDictionary(group => group.Key, group => group.Min(copy => copy.Position));

        return
        [
            .. works
                .OrderBy(work => firstCopyOf.TryGetValue(work.MediaWorkId, out var position) ? position : int.MaxValue)
                .ThenBy(work => work.Title, StringComparer.Ordinal)
                .Select(work => new ReplacementJobWorkDto(
                    work.MediaWorkId,
                    work.Kind,
                    work.Title,
                    work.Year,
                    work.ConfirmedByOwner,
                    [.. identifiers[work.Id].Select(identifier => new ReplacementJobIdentifierDto(identifier.Scheme, identifier.Value, false, identifier.SetByOwner))])),
        ];
    }
}
