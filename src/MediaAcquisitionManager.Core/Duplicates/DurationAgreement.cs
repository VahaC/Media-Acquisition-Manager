namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-20 — whether the measured durations of a possible match's files agree: evidence for the owner, never a verdict
/// (ADR 0012 — a duration matches nothing).
/// </summary>
/// <remarks>
/// The MAM-34 runtime tolerance (<c>LinkSuggestionRanking.RuntimesAgree</c>): 2 minutes or 10 %, whichever is larger —
/// here 10 % of the longest file, so the answer is the same whichever file is named first. Another cut differs by more;
/// another rip of one cut (a studio logo, the credits) by less.
/// </remarks>
public static class DurationAgreement
{
    /// <summary>The least difference that is still agreement: 2 minutes, as <c>LinkSuggestionRanking</c>
    /// allows.</summary>
    private const long ToleranceMinimumMilliseconds = 120_000;

    /// <summary>The share of the longest duration that is still agreement, as <c>LinkSuggestionRanking</c>
    /// allows.</summary>
    private const double ToleranceShare = 0.10;

    /// <summary>
    /// Whether the longest and the shortest of <paramref name="durationsMilliseconds"/> are within the tolerance of
    /// each other; <c>null</c> when fewer than two are known.
    /// </summary>
    public static bool? Of(IReadOnlyCollection<long> durationsMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(durationsMilliseconds);
        if (durationsMilliseconds.Count < 2)
            return null;

        var longest = durationsMilliseconds.Max();
        var shortest = durationsMilliseconds.Min();
        var tolerance = Math.Max(
            ToleranceMinimumMilliseconds, (long)Math.Round(ToleranceShare * longest, MidpointRounding.AwayFromZero));
        return longest - shortest <= tolerance;
    }
}
