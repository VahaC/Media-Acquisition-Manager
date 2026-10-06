namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — how far a batch of links queued by one click (the repair, "Accept all confident") has got.</summary>
/// <param name="BatchId">The batch's id: also usable with <c>GET api/library/tmdb/batches/{id}</c> for the per-work
/// rows.</param>
/// <param name="Total">The links carrying the batch id now.</param>
/// <param name="Done">Of them, those no longer queued or being identified: identified, refused or failed — settled either
/// way.</param>
public sealed record LibraryLinkProgressDto(Guid BatchId, int Total, int Done);
