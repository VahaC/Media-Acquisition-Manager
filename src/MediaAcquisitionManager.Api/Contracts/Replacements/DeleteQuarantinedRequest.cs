namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-26 — the owner's second confirmation of Delete for good (ADR 0030): the names he was shown, every path and size,
/// by their ids. The deletion goes ahead only while they are still exactly what the replacement holds in the quarantine.
/// </summary>
/// <remarks>Nullable and without <c>[Required]</c>, <see cref="ScheduleReplacementRequest"/>'s reason: a missing list
/// reaches the service's sentence, not a validation body.</remarks>
/// <param name="Names">The ids of the names the confirmation listed.</param>
public sealed record DeleteQuarantinedRequest(IReadOnlyList<Guid>? Names = null);
