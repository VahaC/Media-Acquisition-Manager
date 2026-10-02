namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One file of a download job, as its client last listed it (MAM-14).</summary>
/// <remarks>
/// A path relative to the job's folder in the client, as the client names it: what the person sent shows up
/// as, never where it is on any disk — the client's folders and the path mappings stay the owner's.
/// </remarks>
/// <param name="Position">Its place in the client's list, counted from 0.</param>
/// <param name="Path">Its path under the job's folder, <c>/</c>-separated, exactly as the client names
/// it.</param>
/// <param name="SizeBytes">Its size; <c>null</c> when the client didn't say.</param>
/// <param name="Progress">How far the client has downloaded it, from 0 to 1; <c>null</c> when the client
/// didn't say.</param>
/// <param name="IsWanted"><c>false</c> for a file skipped in the client, which it doesn't download;
/// <c>null</c> when the client didn't say.</param>
public sealed record DownloadJobFileDto(int Position, string Path, long? SizeBytes, double? Progress, bool? IsWanted);
