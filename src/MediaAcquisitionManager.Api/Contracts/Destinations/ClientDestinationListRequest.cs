namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-30 — the query string of what a download client offers the Add or Edit destination dialog.</summary>
/// <remarks>A string, for <see cref="DownloadOverwriteCheckRequest"/>'s reason: a malformed id answers the endpoint's
/// <c>{ error }</c> sentence, not a validation body.</remarks>
/// <param name="DestinationId">The destination the Edit dialog edits, whose stored folder a client whose destinations are
/// folders then lists as itself (ADR 0039, D4); left out by the Add dialog. One of another client proposes nothing.</param>
public sealed record ClientDestinationListRequest(string? DestinationId = null);
