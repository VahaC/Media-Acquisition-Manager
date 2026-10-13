namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>One version of a library item.</summary>
/// <param name="SourceVersionId">The library's id of the version, unique across the library — not only
/// inside its item: the snapshot store keys versions by it whatever item lists them, so two items each
/// with a version "1" would be stored as one version (MAM-8, ADR 0008). <c>null</c> when the library
/// has no such notion.</param>
/// <param name="EditionLabel">The edition as the library names it; <c>null</c> when none.</param>
/// <param name="Files">Its files: one, or several stacked parts.</param>
/// <param name="DiscFolderPath">MAM-43 — the folder of a Blu-ray or DVD the library reports as one version (the folder
/// holding <c>BDMV</c> or <c>VIDEO_TS</c>), exactly as the library sent it; <c>null</c> for anything else. The library
/// names no file of it, so <paramref name="Files"/> is empty until the sync lists the disc's clips itself
/// (<c>LibraryDiscClips</c>).</param>
public sealed record LibraryVersion(
    string? SourceVersionId, string? EditionLabel, IReadOnlyList<LibraryFile> Files, string? DiscFolderPath = null);
