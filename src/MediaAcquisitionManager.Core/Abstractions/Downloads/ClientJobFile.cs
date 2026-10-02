namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>One file of a download client's job.</summary>
/// <param name="RelativePath">
/// MAM-14 — the file's path relative to the job's folder in the client, <c>/</c>-separated and exactly as the
/// client names it: never trimmed or cleaned, since it names a real file (qBittorrent's <c>name</c>, which
/// starts with the torrent's root folder under its "Original" layout). It was <c>ServicePath</c> until then,
/// unused: a path relative to the job is not a path as the service sees it.
/// </param>
/// <param name="SizeBytes">Its size; <c>null</c> when unknown.</param>
/// <param name="Progress">From 0 to 1; <c>null</c> when unknown.</param>
/// <param name="IsWanted">Whether the client downloads it; <c>null</c> when it doesn't say.</param>
public sealed record ClientJobFile(string RelativePath, long? SizeBytes, double? Progress, bool? IsWanted);
