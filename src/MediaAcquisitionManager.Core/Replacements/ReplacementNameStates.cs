using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-27 — which names of a replacement's move count as out of the owner's simple hands (ADR 0030). A pure list, as
/// <see cref="ReplacementJobStates"/>'s are.
/// </summary>
public static class ReplacementNameStates
{
    /// <summary>
    /// The states of a name the job took out of its folder and hasn't given back: on its way into the quarantine, in it,
    /// on its way back or out, or deleted from it for good. A job holding one can't be cancelled with Keep both, and a copy
    /// holding one can't be dropped: what left the library comes back through Restore, never by forgetting it.
    /// <see cref="ReplacementNameState.Restored"/> (back where it was) and <see cref="ReplacementNameState.Lost"/> (found
    /// nowhere the app can act on) are not here — a job whose only video is lost would otherwise be stuck for ever —, nor
    /// <see cref="ReplacementNameState.Planned"/> and <see cref="ReplacementNameState.Stayed"/>, still at their old paths.
    /// </summary>
    public static IReadOnlyList<ReplacementNameState> Taken { get; } =
    [
        ReplacementNameState.Moving,
        ReplacementNameState.Quarantined,
        ReplacementNameState.Restoring,
        ReplacementNameState.Deleting,
        ReplacementNameState.Deleted,
    ];
}
