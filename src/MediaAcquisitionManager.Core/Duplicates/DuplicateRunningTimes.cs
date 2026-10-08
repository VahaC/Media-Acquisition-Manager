namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>
/// MAM-37 — the running times of the shortest and the longest measured copy of one work (Vasyl, 2026-10-08): what the
/// owner weighs beside <see cref="Enums.DuplicateDistinctionKind.RunningTimes"/>, given whether or not the copies differ
/// widely (<see cref="RunningTimeSpread"/>). A copy's running time is the sum of its files' measured durations.
/// </summary>
/// <param name="ShortestMilliseconds">The shortest copy's running time.</param>
/// <param name="LongestMilliseconds">The longest copy's running time.</param>
public sealed record DuplicateRunningTimes(long ShortestMilliseconds, long LongestMilliseconds);
