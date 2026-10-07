namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-21 — the owner's Confirm or Dismiss of a possible match of the Duplicates list.</summary>
/// <remarks>
/// The decision is a string, not an enum: a typed field would answer a value that isn't one with a validation body, and
/// a number with the decision of that number, not the <c>{ error }</c> sentence the page shows (the
/// <see cref="LibraryDuplicateListRequest"/> reason).
/// </remarks>
/// <param name="Decision"><c>Confirmed</c> or <c>Dismissed</c> (<see cref="Core.Enums.DuplicateMatchDecision"/>) by name,
/// in any letter case, never its number; required.</param>
public sealed record DuplicateDecisionRequest(string? Decision = null);
