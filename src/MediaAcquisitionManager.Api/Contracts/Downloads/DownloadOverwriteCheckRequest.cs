namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-38 — the query string of the Download dialog's question whether a destination's folder can be
/// checked for files a download would write over.</summary>
/// <remarks>A string, for <see cref="DownloadOptionsRequest"/>'s reason: a missing or malformed id answers the
/// rules' <c>{ error }</c> sentence, not a validation body.</remarks>
/// <param name="DestinationId">The destination the dialog has picked.</param>
public sealed record DownloadOverwriteCheckRequest(string? DestinationId = null);
