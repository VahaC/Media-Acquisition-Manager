namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the review queue of the works without a TMDb id: how many there are, and the one at an index.</summary>
/// <param name="TotalCount">The works in the queue: without a TMDb id, without a link, not marked "none fits".</param>
/// <param name="Index">The place shown, from 0.</param>
/// <param name="Current">The work there, as its dialog; <c>null</c> when the queue is empty or the index past its
/// end.</param>
public sealed record LibraryLinkQueueResponse(int TotalCount, int Index, LibraryLinkDialogResponse? Current);
