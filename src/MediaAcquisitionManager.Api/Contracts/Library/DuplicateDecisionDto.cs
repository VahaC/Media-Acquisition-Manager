using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-21 — the owner's decision on a possible match of the Duplicates list (ADR 0029).</summary>
/// <param name="Decision">Confirmed or dismissed, by name.</param>
/// <param name="DecidedUtc">When it was last given.</param>
/// <param name="DecidedByUserName">Who gave it, for the installation owner only; <c>null</c> for everyone else, as a
/// link's setter's name is.</param>
public sealed record DuplicateDecisionDto(DuplicateMatchDecision Decision, DateTime DecidedUtc, string? DecidedByUserName);
