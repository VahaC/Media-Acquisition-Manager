namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-37 — the running times of the shortest and the longest measured copy of one work: a copy's running time
/// is the sum of its files' measured durations (Vasyl, 2026-10-08).</summary>
/// <param name="ShortestMilliseconds">The shortest copy's running time, in milliseconds.</param>
/// <param name="LongestMilliseconds">The longest copy's running time, in milliseconds.</param>
public sealed record DuplicateRunningTimesDto(long ShortestMilliseconds, long LongestMilliseconds);
