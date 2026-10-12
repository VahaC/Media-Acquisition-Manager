using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-21 — what a decision on a possible match may be, as the Duplicates list's filter and the owner's Confirm and
/// Dismiss send it, and what a group key looks like. One readable sentence for a value refused, <c>null</c> for one
/// accepted — the MAM-4 rules-class pattern. MAM-40 — and which classes take which decision, and when a decision on kept
/// copies is in force.
/// </summary>
/// <remarks>
/// A decision is read by name alone, in any letter case: <c>Enum.TryParse</c> would also take <c>1</c>, <c>-1</c> and
/// <c>Confirmed, Dismissed</c>, which it ORs into one value (the <c>DownloadJobListRules</c> rule).
/// </remarks>
public static class DuplicateDecisionRules
{
    /// <summary>
    /// MAM-40 — the sentence a confirmation of copies of one work, or of byte-identical copies, gets: their bytes or
    /// registry ids already make them copies, so the owner's word is only whether to keep them as they are (Keep both).
    /// </summary>
    public const string CopiesAreNotConfirmedError =
        "These files are copies by their bytes or registry ids, which no one has to confirm: keep them as they are, or remove a copy.";

    /// <summary>
    /// MAM-40 — the sentence any decision on one file's names gets: removing a name frees nothing until the file's last
    /// name goes, so there is no copy to keep or to remove.
    /// </summary>
    public const string OneFileIsNotDecidedError =
        "These are names of one file, not copies of it: removing a name frees nothing, so there is nothing to keep or remove.";

    /// <summary>How much of a value that isn't a decision a sentence repeats: the longest decision is 9 characters, and a
    /// pasted page of text shouldn't come back whole (the list rules' rule).</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// MAM-40 — what joins a kept group's member path keys into <c>KeptMembers</c>. A path that holds a line break itself
    /// would read back as two names that match none, so its group is asked about again: the safe side.
    /// </summary>
    private const char MemberSeparator = '\n';

    private const string MissingError = "A decision is needed: Confirmed or Dismissed.";

    /// <summary>
    /// The list's filter: the sentence for <paramref name="decision"/> when it is not a decision's name, or <c>null</c>
    /// with the decision in <paramref name="matchDecision"/> — <c>null</c> for a blank value, which filters nothing.
    /// </summary>
    public static string? ValidateFilter(string? decision, out DuplicateMatchDecision? matchDecision)
    {
        matchDecision = null;
        if (string.IsNullOrWhiteSpace(decision))
            return null;

        var trimmed = decision.Trim();
        var name = Enum.GetNames<DuplicateMatchDecision>().FirstOrDefault(candidate => candidate.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return $"“{Echo(trimmed)}” is not a decision. Use Confirmed or Dismissed.";

        matchDecision = Enum.Parse<DuplicateMatchDecision>(name);
        return null;
    }

    /// <summary>
    /// The owner's decision: the sentence for <paramref name="decision"/> missing, blank or not a decision's name, or
    /// <c>null</c> with the decision in <paramref name="matchDecision"/>.
    /// </summary>
    public static string? Validate(string? decision, out DuplicateMatchDecision matchDecision)
    {
        matchDecision = default;
        if (string.IsNullOrWhiteSpace(decision))
            return MissingError;
        if (ValidateFilter(decision, out var parsed) is { } error)
            return error;

        matchDecision = parsed!.Value;
        return null;
    }

    /// <summary>
    /// MAM-40 — the sentence when a group of <paramref name="groupClass"/> can't be given <paramref name="decision"/> —
    /// or, with <c>null</c>, can't have its decision undone —; <c>null</c> when it can. A possible match is confirmed or
    /// dismissed (ADR 0029); copies of one work and byte-identical copies are only kept as they are, which is
    /// <see cref="DuplicateMatchDecision.Dismissed"/> (Keep both, Vasyl, 2026-10-12); one file's names are never decided.
    /// </summary>
    public static string? RefusalOf(DuplicateClass groupClass, DuplicateMatchDecision? decision) => groupClass switch
    {
        DuplicateClass.Possible => null,
        DuplicateClass.SameWork or DuplicateClass.ByteIdentical when decision is null or DuplicateMatchDecision.Dismissed => null,
        DuplicateClass.SameWork or DuplicateClass.ByteIdentical => CopiesAreNotConfirmedError,
        _ => OneFileIsNotDecidedError,
    };

    /// <summary>
    /// MAM-40 — what a decision on <paramref name="group"/> remembers of its members: for kept copies, its
    /// <see cref="DuplicateGroup.MemberPathKeys"/> joined by line breaks, as their key doesn't change when a file joins (a
    /// same-work key holds the works, a byte-identical one the hash); <c>null</c> for a possible match, whose key already
    /// names every subject.
    /// </summary>
    public static string? KeptMembersOf(DuplicateGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Class is DuplicateClass.SameWork or DuplicateClass.ByteIdentical ? string.Join(MemberSeparator, group.MemberPathKeys) : null;
    }

    /// <summary>
    /// MAM-40 — whether a decision stored with <paramref name="keptMembers"/> holds for <paramref name="group"/>, the group
    /// its key names now: always for a possible match (ADR 0029); for kept copies, while every member the group has now
    /// was among those kept — a new file joining asks the owner again, a member leaving changes nothing (Vasyl,
    /// 2026-10-12); never for one file's names. A decision that doesn't hold is ignored, never deleted: it holds again if
    /// the group comes back to members it covers, and keeping the group again overwrites it.
    /// </summary>
    public static bool IsInForce(DuplicateGroup group, string? keptMembers)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Class == DuplicateClass.Possible)
            return true;
        if (group.Class is not (DuplicateClass.SameWork or DuplicateClass.ByteIdentical) || keptMembers is null)
            return false;

        var kept = keptMembers.Split(MemberSeparator).ToHashSet(StringComparer.Ordinal);
        return group.MemberPathKeys.All(kept.Contains);
    }

    /// <summary>
    /// Whether <paramref name="key"/> has the shape of every group key — exactly
    /// <see cref="DuplicateMatchDecisionEntity.GroupKeyLength"/> lowercase hex characters — so a key no group can have is
    /// refused without reading the library.
    /// </summary>
    public static bool IsGroupKey(string? key) =>
        key is { Length: DuplicateMatchDecisionEntity.GroupKeyLength } && key.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
