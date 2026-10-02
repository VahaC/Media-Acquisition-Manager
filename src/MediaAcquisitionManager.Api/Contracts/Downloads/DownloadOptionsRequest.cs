namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The query string of the Download dialog's choices (MAM-13).</summary>
/// <remarks>
/// A string, for the <see cref="SearchReleaseListRequest"/> reason: a missing or malformed id answers the
/// rules' <c>{ error }</c> sentence, not a validation body.
/// </remarks>
/// <param name="ReleaseId">The release to send, one of the caller's own searches'.</param>
public sealed record DownloadOptionsRequest(string? ReleaseId = null);
