namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-25 — what one <see cref="IMediaLibraryFileLookup.RequestScanAsync"/> got the library to take: its request to look
/// at the paths. Nothing more: the library answers before it looks (Jellyfin at once, whatever the paths are), so whether
/// it found a file is for <see cref="IMediaLibraryFileLookup.ReadFilesAsync"/> to tell.
/// </summary>
/// <param name="PathCount">How many paths the request named, all in one request.</param>
public sealed record LibraryScanRequest(int PathCount);
