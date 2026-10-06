namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-34 — what the repair of the links the library was never confirmed to show (Vasyl's decisions of 2026-10-06) has to
/// identify: what it would (<c>GET api/library/tmdb/catch-up</c>), or what it queued (<c>POST</c>, 202).
/// </summary>
/// <param name="ToIdentify">GET: how many links a click would queue now. POST: how many this click queued.</param>
/// <param name="Progress">The batch under way — the links <c>Queued</c> or <c>Writing</c> that carry a batch id — or
/// <c>null</c> when nothing is pending. POST: the batch this click made (<c>Done</c> 0), <c>null</c> when it queued
/// nothing.</param>
public sealed record LibraryLinkCatchUpResponse(int ToIdentify, LibraryLinkProgressDto? Progress);
