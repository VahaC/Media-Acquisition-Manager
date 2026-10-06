namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — what "Accept all confident" did, work by work; polled again by its id while the library writes are
/// under way.</summary>
/// <param name="BatchId">The click's id, shared by every link it set.</param>
/// <param name="Items">Each work given, in the order given, with what happened to it.</param>
public sealed record LibraryLinkBatchResponse(Guid BatchId, IReadOnlyList<LibraryLinkBatchItemDto> Items);
