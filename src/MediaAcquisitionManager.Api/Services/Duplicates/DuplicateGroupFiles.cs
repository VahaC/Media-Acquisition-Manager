using MediaAcquisitionManager.Api.Services.CopyRemovals;
using MediaAcquisitionManager.Core.Duplicates;

namespace MediaAcquisitionManager.Api.Services.Duplicates;

/// <summary>MAM-40 — a duplicate group as the owner's Remove reads it (<see cref="ILibraryDuplicateService.FindForRemovalAsync"/>):
/// the group, each of its files as the removal rules read them, and the heading the page shows it by.</summary>
/// <param name="Group">The group, worked out as the list does.</param>
/// <param name="Files">Its files, in the group's order (<see cref="DuplicateGroup.FileIds"/>).</param>
/// <param name="Heading">What its card is headed by: "The Gods Must Be Crazy (1980)", an episode's "Clarkson's Farm ·
/// S02E03 · Title", or the first file's own name for a group no library reported.</param>
public sealed record DuplicateGroupFiles(DuplicateGroup Group, IReadOnlyList<CopyRemovalCandidate> Files, string Heading);
