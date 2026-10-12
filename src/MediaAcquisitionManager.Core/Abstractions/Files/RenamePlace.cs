namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>MAM-26 — whether a rename into the quarantine can work in one folder, as
/// <see cref="IFileQuarantine.DescribeRenamePlacesAsync"/> found it.</summary>
/// <param name="FolderAppPath">The folder, as asked.</param>
/// <param name="Problems">What stands in the way; none means a rename can work there.</param>
/// <param name="WriteAccess">MAM-39 — when <paramref name="Problems"/> holds <see cref="RenamePlaceProblem.NotWritable"/>,
/// the folder the app's user may not write to with its owner's ids and the app's; <c>null</c> otherwise, and when they
/// couldn't be read.</param>
public sealed record RenamePlace(
    string FolderAppPath,
    IReadOnlyList<RenamePlaceProblem> Problems,
    RenamePlaceWriteAccess? WriteAccess = null);
