namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — the query string of the Download dialog's question what of a release the owner may replace through a
/// destination (ADR 0032).</summary>
/// <remarks>Strings, for <see cref="DownloadOptionsRequest"/>'s reason: a missing or malformed value answers the rules'
/// <c>{ error }</c> sentence, not a validation body.</remarks>
/// <param name="ReleaseId">The release, one of the caller's own searches'.</param>
/// <param name="DestinationId">The destination the dialog has picked.</param>
/// <param name="Holdings">"true" to read every download client's torrents for the held marks (ADR 0031); left out or
/// "false", none is read.</param>
public sealed record ReplacementOptionsRequest(string? ReleaseId = null, string? DestinationId = null, string? Holdings = null);
