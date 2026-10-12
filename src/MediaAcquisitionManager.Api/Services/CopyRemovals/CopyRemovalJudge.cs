using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.CopyRemovals;

/// <summary>
/// MAM-40 — <see cref="CopyRemovalRules"/> with what they read from the database once per request: the gates and the files
/// open replacements and removals hold. The Duplicates list judges every file of its page through one, the Remove request
/// its one file.
/// </summary>
/// <param name="Gate">The switch, the allowed roots and the ticks, read fresh (ADR 0033).</param>
/// <param name="Holds">What open replacements and removals hold.</param>
public sealed record CopyRemovalJudge(ReplacementGateState Gate, CopyRemovalHolds Holds)
{
    /// <summary>Why <paramref name="file"/> of a group of <paramref name="groupClass"/> about <paramref name="groupWorkIds"/>,
    /// whose files are <paramref name="members"/>, can't be removed by what is stored; <c>null</c> when it may.</summary>
    public string? ReasonOf(
        DuplicateClass groupClass, IReadOnlyCollection<Guid> groupWorkIds, CopyRemovalCandidate file, IReadOnlyList<CopyRemovalCandidate> members) =>
        CopyRemovalRules.ReasonOf(groupClass, groupWorkIds, file, members, Gate, Holds);
}
