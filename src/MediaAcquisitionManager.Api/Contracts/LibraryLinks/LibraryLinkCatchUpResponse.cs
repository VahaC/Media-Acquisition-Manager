namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>
/// MAM-34 — what the catch-up of the links set before the app wrote series and episodes and asked for the metadata lookup
/// (Vasyl's decision of 2026-10-06) has to send: what it would send (<c>GET api/library/tmdb/catch-up</c>), or what it
/// queued (<c>POST</c>).
/// </summary>
/// <param name="ToWrite">The links that write ids of their own and the library doesn't hold yet: kept in the app under
/// the first rules (a series', an episode's with its season and episode on a film item), or not written — a catch-up's
/// failed write among them.</param>
/// <param name="ToLookUp">The links written into the library whose item the library was never asked to look up.</param>
public sealed record LibraryLinkCatchUpResponse(int ToWrite, int ToLookUp);
