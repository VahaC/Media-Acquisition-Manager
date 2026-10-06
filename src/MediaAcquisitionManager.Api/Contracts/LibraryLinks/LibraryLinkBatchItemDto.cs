using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one work of an "Accept all confident" click.</summary>
/// <param name="WorkId">The library work.</param>
/// <param name="Outcome">Whether it was linked or skipped.</param>
/// <param name="Reason">Why it was skipped, as a sentence; <c>null</c> when it was linked.</param>
/// <param name="Links">Its links, one per library item, with their write states as they are now.</param>
public sealed record LibraryLinkBatchItemDto(Guid WorkId, LinkBatchOutcome Outcome, string? Reason, IReadOnlyList<LibraryLinkRowDto> Links);
