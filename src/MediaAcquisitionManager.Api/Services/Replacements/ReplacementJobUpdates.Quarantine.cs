using MediaAcquisitionManager.Api.Services.Files;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// MAM-26 — the mover's writes (ADR 0030): its claim of a verified job and its stop before one, the intent and the outcome
// of each name it renames, what its recovery finds of a name a restart cut, and the job's end. Each a conditional UPDATE of
// a row still in the state the mover read, never cancelled — the same rules as the writes beside them. MAM-40 — the names'
// own writes are generic over QuarantinedNameEntity: a removal's names are written by the same ones.
public static partial class ReplacementJobUpdates
{
    /// <summary>
    /// MAM-26 — claims the replacement <paramref name="replacementJobId"/> for its move while it is still
    /// <see cref="ReplacementJobState.Verified"/>: <see cref="ReplacementJobState.Quarantining"/> at
    /// <paramref name="startedUtc"/>, no reason, and <see cref="ReplacementJobEntity.QuarantineCheckedUtc"/> then — the mark
    /// of a job the mover's stage holds, which the owner's Retry asks for. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> StartQuarantineAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, DateTime startedUtc) =>
        jobs.Where(job => job.Id == replacementJobId && job.State == ReplacementJobState.Verified)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.Quarantining)
                    .SetProperty(job => job.Reason, (string?)null)
                    .SetProperty(job => job.StateChangedUtc, startedUtc)
                    .SetProperty(job => job.QuarantineCheckedUtc, startedUtc),
                CancellationToken.None);

    /// <summary>
    /// MAM-26 — stops the replacement <paramref name="replacementJobId"/> for the owner's review before anything of it
    /// moved, while it is still <see cref="ReplacementJobState.Verified"/> — its checks 5 and 6 found a reason —:
    /// <see cref="ReplacementJobState.NeedsReview"/> at <paramref name="changedUtc"/> with <paramref name="reason"/>, and
    /// <see cref="ReplacementJobEntity.QuarantineCheckedUtc"/> then, so Retry is the owner's way on. How many it wrote (0 or
    /// 1).
    /// </summary>
    public static Task<int> StopBeforeQuarantineAsync(this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, string reason, DateTime changedUtc)
    {
        var storedReason = Stored(reason);
        return jobs.Where(job => job.Id == replacementJobId && job.State == ReplacementJobState.Verified)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.State, ReplacementJobState.NeedsReview)
                    .SetProperty(job => job.Reason, storedReason)
                    .SetProperty(job => job.StateChangedUtc, changedUtc)
                    .SetProperty(job => job.QuarantineCheckedUtc, changedUtc),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-26 — ends the move of the replacement <paramref name="replacementJobId"/> while it is still
    /// <see cref="ReplacementJobState.Quarantining"/>: <see cref="ReplacementJobState.Quarantined"/>, from
    /// <paramref name="changedUtc"/> on, <see cref="ReplacementJobState.Completed"/> — a Retry's run that found nothing in
    /// the quarantine and every video deleted from it for good —, or <see cref="ReplacementJobState.NeedsReview"/> with
    /// <paramref name="reason"/>; with a library notice owed from then when <paramref name="noticeDue"/> — something left
    /// the library —, and the one owed before kept otherwise. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> SettleQuarantineAsync(
        this IQueryable<ReplacementJobEntity> jobs, Guid replacementJobId, ReplacementJobState state, string? reason, DateTime changedUtc, bool noticeDue)
    {
        if (state is not (ReplacementJobState.Quarantined or ReplacementJobState.Completed or ReplacementJobState.NeedsReview))
            throw new ArgumentOutOfRangeException(nameof(state), state, "A move ends Quarantined, Completed or NeedsReview.");

        var storedReason = Stored(reason);
        return jobs.Where(job => job.Id == replacementJobId && job.State == ReplacementJobState.Quarantining)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters
                        .SetProperty(job => job.State, state)
                        .SetProperty(job => job.Reason, storedReason)
                        .SetProperty(job => job.StateChangedUtc, changedUtc);
                    if (state == ReplacementJobState.Quarantined)
                        setters.SetProperty(job => job.QuarantinedUtc, changedUtc);
                    if (noticeDue)
                        setters.SetProperty(job => job.LibraryNoticeDueUtc, changedUtc);
                },
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-26 — settles the replacement <paramref name="replacementJobId"/>, found in <paramref name="observedState"/> —
    /// one of <see cref="ReplacementJobStates.Unsettled"/> — with no one in this process writing for it, as a restart or a
    /// crash cut it, at <paramref name="changedUtc"/>: <see cref="ReplacementJobState.NeedsReview"/> with
    /// <paramref name="reason"/>, <see cref="ReplacementJobEntity.QuarantineCheckedUtc"/> set then unless it was already, so
    /// the owner's Retry is offered; or, for a restore or a deletion the cut came after the last name of,
    /// <paramref name="state"/> — <see cref="ReplacementJobState.Restored"/> from <c>Restoring</c>,
    /// <see cref="ReplacementJobState.Completed"/> from <c>Deleting</c> —, as the write itself would have ended it. A library
    /// notice owed from then when <paramref name="noticeDue"/>. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> SettleUnsettledAsync(
        this IQueryable<ReplacementJobEntity> jobs,
        Guid replacementJobId,
        ReplacementJobState observedState,
        ReplacementJobState state,
        string? reason,
        DateTime changedUtc,
        bool noticeDue)
    {
        if (!ReplacementJobStates.Unsettled.Contains(observedState))
            throw new ArgumentOutOfRangeException(nameof(observedState), observedState, "Only a job a write was cut in is settled here.");
        if (!(state == ReplacementJobState.NeedsReview
            || (observedState, state) is (ReplacementJobState.Restoring, ReplacementJobState.Restored) or (ReplacementJobState.Deleting, ReplacementJobState.Completed)))
            throw new ArgumentOutOfRangeException(nameof(state), state, "A cut write ends where the write would have, or in review.");

        var storedReason = Stored(reason);
        DateTime? checkedUtc = state == ReplacementJobState.NeedsReview ? changedUtc : null;
        return jobs.Where(job => job.Id == replacementJobId && job.State == observedState)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters
                        .SetProperty(job => job.State, state)
                        .SetProperty(job => job.Reason, storedReason)
                        .SetProperty(job => job.StateChangedUtc, changedUtc)
                        .SetProperty(job => job.QuarantineCheckedUtc, job => job.QuarantineCheckedUtc ?? checkedUtc);
                    if (noticeDue)
                        setters.SetProperty(job => job.LibraryNoticeDueUtc, changedUtc);
                },
                CancellationToken.None);
    }

    // ── the names ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MAM-26 — the intent of the mover's rename of the name <paramref name="nameId"/>: from
    /// <see cref="ReplacementNameState.Planned"/> to <see cref="ReplacementNameState.Moving"/>, committed before the call,
    /// so a restart between the two finds it in the middle. How many it wrote (0 or 1). MAM-40 — a replacement's name or a
    /// removal's alike (<see cref="QuarantinedNameEntity"/>).
    /// </summary>
    public static Task<int> StartNameMoveAsync<TName>(this IQueryable<TName> names, Guid nameId)
        where TName : QuarantinedNameEntity =>
        names.Where(name => name.Id == nameId && name.State == ReplacementNameState.Planned)
            .ExecuteUpdateAsync(setters => setters.SetProperty(name => name.State, ReplacementNameState.Moving), CancellationToken.None);

    /// <summary>
    /// MAM-26 — the outcome of a rename into the quarantine that was done: the name <paramref name="nameId"/>,
    /// still <see cref="ReplacementNameState.Moving"/>, is <see cref="ReplacementNameState.Quarantined"/> since
    /// <paramref name="movedUtc"/>, with what the file system said of it at its new name (<paramref name="facts"/>) — what
    /// Restore and Delete for good expect there. Written whatever the job's state by then: the file moved. How many it
    /// wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordNameMovedAsync<TName>(this IQueryable<TName> names, Guid nameId, FileFacts facts, DateTime movedUtc)
        where TName : QuarantinedNameEntity
    {
        ArgumentNullException.ThrowIfNull(facts);
        var (deviceId, inode, sizeBytes, modifiedUtc, linkCount) = (facts.DeviceId, facts.Inode, facts.SizeBytes, facts.ModifiedUtc, facts.LinkCount);
        return names.Where(name => name.Id == nameId && name.State == ReplacementNameState.Moving)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(name => name.State, ReplacementNameState.Quarantined)
                    .SetProperty(name => name.MovedUtc, movedUtc)
                    // A rename keeps the identity; a platform that gave none keeps the one read before.
                    .SetProperty(name => name.DeviceId, name => deviceId ?? name.DeviceId)
                    .SetProperty(name => name.Inode, name => inode ?? name.Inode)
                    .SetProperty(name => name.SizeBytes, sizeBytes)
                    .SetProperty(name => name.ModifiedUtc, modifiedUtc)
                    .SetProperty(name => name.LinkCount, linkCount)
                    .SetProperty(name => name.Problem, (string?)null),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-26 — the outcome of a rename into the quarantine that wasn't done: the name <paramref name="nameId"/>,
    /// still <see cref="ReplacementNameState.Moving"/>, stays where it was (<see cref="ReplacementNameState.Stayed"/>),
    /// with the write's own sentence as <paramref name="problem"/>; or, when the rename moved another file put at the name
    /// after the last look, it is <see cref="ReplacementNameState.Lost"/> with why. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordNameNotMovedAsync<TName>(this IQueryable<TName> names, Guid nameId, ReplacementNameState state, string problem)
        where TName : QuarantinedNameEntity
    {
        if (state is not (ReplacementNameState.Stayed or ReplacementNameState.Lost))
            throw new ArgumentOutOfRangeException(nameof(state), state, "A rename not done leaves a name Stayed, or Lost.");

        var storedProblem = FileInspectionRunUpdates.Cut(problem, QuarantinedNameEntity.ProblemMaximumLength);
        return names.Where(name => name.Id == nameId && name.State == ReplacementNameState.Moving)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(name => name.State, state)
                    .SetProperty(name => name.Problem, storedProblem),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-26 — a companion an earlier run left waiting, <paramref name="nameId"/>, still
    /// <see cref="ReplacementNameState.Planned"/>, that a Retry's checks found shouldn't move: it stays where it is
    /// (<see cref="ReplacementNameState.Stayed"/>) or is gone from there (<see cref="ReplacementNameState.Lost"/>), with
    /// <paramref name="problem"/> saying why. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordNameLeftWaitingAsync<TName>(this IQueryable<TName> names, Guid nameId, ReplacementNameState state, string problem)
        where TName : QuarantinedNameEntity
    {
        if (state is not (ReplacementNameState.Stayed or ReplacementNameState.Lost))
            throw new ArgumentOutOfRangeException(nameof(state), state, "A name left waiting stays, or is lost.");

        var storedProblem = FileInspectionRunUpdates.Cut(problem, QuarantinedNameEntity.ProblemMaximumLength);
        return names.Where(name => name.Id == nameId && name.State == ReplacementNameState.Planned)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(name => name.State, state)
                    .SetProperty(name => name.Problem, storedProblem),
                CancellationToken.None);
    }

    /// <summary>
    /// MAM-26 — what the mover's recovery found of the name <paramref name="nameId"/>, left in
    /// <paramref name="observedState"/> — the intent of a write a restart cut —: <paramref name="state"/>, with
    /// <paramref name="problem"/> (why it is lost; <c>null</c> otherwise) and, for a name found where the write was taking
    /// it, the write's time as <paramref name="foundUtc"/> — the moment it was seen there, as the rename's own is unknown.
    /// How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> RecordNameFoundAsync<TName>(
        this IQueryable<TName> names,
        Guid nameId,
        ReplacementNameState observedState,
        ReplacementNameState state,
        string? problem,
        DateTime foundUtc)
        where TName : QuarantinedNameEntity
    {
        var storedProblem = problem is null ? null : FileInspectionRunUpdates.Cut(problem, QuarantinedNameEntity.ProblemMaximumLength);
        return names.Where(name => name.Id == nameId && name.State == observedState)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters
                        .SetProperty(name => name.State, state)
                        .SetProperty(name => name.Problem, storedProblem);
                    switch ((observedState, state))
                    {
                        case (ReplacementNameState.Moving, ReplacementNameState.Quarantined):
                            setters.SetProperty(name => name.MovedUtc, foundUtc);
                            break;
                        case (ReplacementNameState.Restoring, ReplacementNameState.Restored):
                            setters.SetProperty(name => name.RestoredUtc, foundUtc);
                            break;
                        case (ReplacementNameState.Deleting, ReplacementNameState.Deleted):
                            setters.SetProperty(name => name.DeletedUtc, foundUtc);
                            break;
                    }
                },
                CancellationToken.None);
    }
}
