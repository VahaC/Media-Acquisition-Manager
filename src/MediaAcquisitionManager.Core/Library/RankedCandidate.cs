namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — a TMDb candidate of a library work, with why it ranks where it does.</summary>
/// <param name="Candidate">The candidate.</param>
/// <param name="Evidence">What speaks for and against it.</param>
/// <param name="IsConfident">Whether it is the work's one confident suggestion (decision 8), which "Accept all
/// confident" links without the owner looking at it.</param>
public sealed record RankedCandidate(LinkCandidateFacts Candidate, LinkEvidence Evidence, bool IsConfident);
