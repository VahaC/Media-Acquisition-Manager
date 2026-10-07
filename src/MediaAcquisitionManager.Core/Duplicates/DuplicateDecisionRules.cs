using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-21 — what a decision on a possible match may be, as the Duplicates list's filter and the owner's Confirm and
/// Dismiss send it, and what a group key looks like. One readable sentence for a value refused, <c>null</c> for one
/// accepted — the MAM-4 rules-class pattern.
/// </summary>
/// <remarks>
/// A decision is read by name alone, in any letter case: <c>Enum.TryParse</c> would also take <c>1</c>, <c>-1</c> and
/// <c>Confirmed, Dismissed</c>, which it ORs into one value (the <c>DownloadJobListRules</c> rule).
/// </remarks>
public static class DuplicateDecisionRules
{
    /// <summary>The sentence a group of another class gets: only a title and a year need the owner's word.</summary>
    public const string NotAPossibleMatchError =
        "Only a possible match is confirmed or dismissed: this group is one by its files' identity, bytes or registry ids, which no one has to confirm.";

    /// <summary>How much of a value that isn't a decision a sentence repeats: the longest decision is 9 characters, and a
    /// pasted page of text shouldn't come back whole (the list rules' rule).</summary>
    private const int EchoLength = 32;

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
    /// Whether <paramref name="key"/> has the shape of every group key — exactly
    /// <see cref="DuplicateMatchDecisionEntity.GroupKeyLength"/> lowercase hex characters — so a key no group can have is
    /// refused without reading the library.
    /// </summary>
    public static bool IsGroupKey(string? key) =>
        key is { Length: DuplicateMatchDecisionEntity.GroupKeyLength } && key.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
