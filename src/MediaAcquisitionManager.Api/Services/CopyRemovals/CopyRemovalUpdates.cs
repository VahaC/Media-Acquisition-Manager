using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — the writes that move a removal on, each a conditional <c>UPDATE</c> of a removal still in the state its writer
/// read (<c>ReplacementJobUpdates</c>' pattern): whoever writes first wins — the owner's request, or the quarantine's pass
/// settling a cut one — and the other's write, made against a state the removal no longer has, changes nothing. Never
/// cancelled: once a state is decided it is written, even while the app shuts down. Its names are written by
/// <c>ReplacementJobUpdates</c>' own name writes, generic over <see cref="QuarantinedNameEntity"/>.
/// </summary>
public static class CopyRemovalUpdates
{
    /// <summary>
    /// Moves the removal <paramref name="copyRemovalId"/>, read as <paramref name="observedState"/>, to
    /// <paramref name="state"/> at <paramref name="changedUtc"/> with <paramref name="reason"/> or none — fitted to its
    /// column by the column's converter (MAM-47) —; reaching <see cref="CopyRemovalState.Quarantined"/> stamps
    /// <see cref="CopyRemovalEntity.QuarantinedUtc"/>, and a library notice is owed from <paramref name="libraryNoticeDueUtc"/>
    /// when given, the one owed before kept otherwise. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> MoveAsync(
        this IQueryable<CopyRemovalEntity> removals,
        Guid copyRemovalId,
        CopyRemovalState observedState,
        CopyRemovalState state,
        Message? reason,
        DateTime changedUtc,
        DateTime? libraryNoticeDueUtc = null) =>
        removals.Where(removal => removal.Id == copyRemovalId && removal.State == observedState)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters
                        .SetProperty(removal => removal.State, state)
                        .SetProperty(removal => removal.Reason, reason)
                        .SetProperty(removal => removal.StateChangedUtc, changedUtc)
                        .SetProperty(removal => removal.LibraryNoticeDueUtc, removal => libraryNoticeDueUtc ?? removal.LibraryNoticeDueUtc);
                    if (state == CopyRemovalState.Quarantined)
                        setters.SetProperty(removal => removal.QuarantinedUtc, changedUtc);
                },
                CancellationToken.None);

    /// <summary>
    /// Holds the removal <paramref name="copyRemovalId"/>, read as <paramref name="observedState"/>, for writes that go with
    /// it in the caller's transaction and changes nothing: an <c>UPDATE</c> of its state to itself, which holds SQLite's
    /// write lock until the caller commits and counts the removal only while it still has that state. How many it held (0
    /// or 1).
    /// </summary>
    public static Task<int> HoldAsync(this IQueryable<CopyRemovalEntity> removals, Guid copyRemovalId, CopyRemovalState observedState) =>
        removals.Where(removal => removal.Id == copyRemovalId && removal.State == observedState)
            .ExecuteUpdateAsync(setters => setters.SetProperty(removal => removal.State, observedState), CancellationToken.None);

    /// <summary>
    /// Settles the library notice the removal <paramref name="copyRemovalId"/> owes since <paramref name="dueUtc"/>, while
    /// that is still the notice it owes: one a restore made due again meanwhile, at a later time, stays owed for its own
    /// paths. Never the removal's state. How many it wrote (0 or 1).
    /// </summary>
    public static Task<int> SettleLibraryNoticeAsync(this IQueryable<CopyRemovalEntity> removals, Guid copyRemovalId, DateTime dueUtc) =>
        removals.Where(removal => removal.Id == copyRemovalId && removal.LibraryNoticeDueUtc == dueUtc)
            .ExecuteUpdateAsync(setters => setters.SetProperty(removal => removal.LibraryNoticeDueUtc, (DateTime?)null), CancellationToken.None);
}
