using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — how far a write over names in the quarantine got, counted the one way a replacement's (MAM-26) and a
/// removal's are: what <see cref="ReplacementNameTally"/> words.
/// </summary>
internal static class QuarantineNameCounts
{
    /// <summary>The quarantined names among <paramref name="states"/>, of every name a move planned — waiting, moving,
    /// moved, staying or lost; a name put back or deleted for good is no longer part of a move.</summary>
    public static (int Count, int Total) Moved(IReadOnlyList<ReplacementNameState> states) =>
        (states.Count(state => state == ReplacementNameState.Quarantined),
            states.Count(state => state is not (ReplacementNameState.Restored or ReplacementNameState.Deleted)));

    /// <summary>The names back where they were, of every name a restore was putting back: in the quarantine, back, or
    /// lost on the way.</summary>
    public static (int Count, int Total) Restored(IReadOnlyList<ReplacementNameState> states) =>
        (states.Count(state => state == ReplacementNameState.Restored),
            states.Count(state => state is ReplacementNameState.Quarantined or ReplacementNameState.Restored or ReplacementNameState.Lost));

    /// <summary>The names deleted for good, of every name a deletion was taking: in the quarantine, deleted, or lost on the
    /// way.</summary>
    public static (int Count, int Total) Deleted(IReadOnlyList<ReplacementNameState> states) =>
        (states.Count(state => state == ReplacementNameState.Deleted),
            states.Count(state => state is ReplacementNameState.Quarantined or ReplacementNameState.Deleted or ReplacementNameState.Lost));
}
