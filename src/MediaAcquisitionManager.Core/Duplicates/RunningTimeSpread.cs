namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-37 — whether the copies of one work run so differently that the owner must look (Vasyl, 2026-10-08): the
/// shortest measured copy under three quarters of the longest. Evidence that something else sits under the work's ids —
/// a featurette, another film a library gave them — never a verdict: nothing is split, and no copy is called redundant.
/// </summary>
/// <remarks>
/// <para>
/// Three quarters, from the library's own copies (production, 2026-10-08): the featurette filed with «Ніж у воді» runs
/// 27.9 % of the film and Blade Runner 2049 under the 1982 film's ids makes the Final Cut 71.7 % of it, while no other
/// cut runs shorter than Alien³'s theatrical one, 79.3 % of the Special Assembly Cut.
/// </para>
/// <para>
/// Not <see cref="DurationAgreement"/>'s 2 minutes or 10 %: that tells another rip from another cut, and two cuts are
/// still copies of one work. Compared in whole milliseconds as 4 × shortest &lt; 3 × longest, so no floating point
/// rounds a boundary either way; exactly three quarters is not under.
/// </para>
/// </remarks>
public static class RunningTimeSpread
{
    /// <summary>Three quarters' numerator: the shortest copy must run at least this many
    /// <see cref="ShareDenominator"/>ths of the longest.</summary>
    private const long ShareNumerator = 3;

    /// <summary>Three quarters' denominator.</summary>
    private const long ShareDenominator = 4;

    /// <summary>
    /// Whether the shortest copy's running time, <paramref name="shortestMilliseconds"/>, is under three quarters of the
    /// longest copy's, <paramref name="longestMilliseconds"/>.
    /// </summary>
    public static bool IsWide(long shortestMilliseconds, long longestMilliseconds) =>
        ShareDenominator * shortestMilliseconds < ShareNumerator * longestMilliseconds;
}
