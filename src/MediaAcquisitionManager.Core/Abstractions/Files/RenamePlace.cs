namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>MAM-26 — whether a rename into the quarantine can work in one folder, as
/// <see cref="IFileQuarantine.DescribeRenamePlacesAsync"/> found it.</summary>
/// <param name="FolderAppPath">The folder, as asked.</param>
/// <param name="Problems">What stands in the way; none means a rename can work there.</param>
public sealed record RenamePlace(string FolderAppPath, IReadOnlyList<RenamePlaceProblem> Problems);
