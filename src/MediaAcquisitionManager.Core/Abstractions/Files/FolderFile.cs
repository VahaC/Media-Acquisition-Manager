namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>MAM-26 — one regular file beside an old copy, as <see cref="IFileQuarantine.ListFolderFilesAsync"/> found
/// it.</summary>
/// <param name="Name">Its own name, without its folder.</param>
/// <param name="AppPath">Its absolute path inside the app.</param>
/// <param name="Facts">What the file system says about it.</param>
public sealed record FolderFile(string Name, string AppPath, FileFacts Facts);
