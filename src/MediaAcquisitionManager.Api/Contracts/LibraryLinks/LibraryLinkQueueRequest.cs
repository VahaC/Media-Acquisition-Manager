namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the query string of the review queue: which of the works without a TMDb id to show.</summary>
/// <remarks>A string, for the <see cref="LibraryLinkRequest"/> reason (R18).</remarks>
/// <param name="Index">The place in the queue, from 0; blank for the first.</param>
public sealed record LibraryLinkQueueRequest(string? Index = null);
