using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — one library folder on the Media folders page's replacement list, with its tick (ADR 0033).</summary>
/// <param name="AppPath">The folder inside the app, in canonical form; what the page sends back to keep it
/// ticked.</param>
/// <param name="LibraryNames">Every library that lists the folder ("Фільми"); none for
/// <see cref="ReplacementFolderStatus.NoLongerALibraryFolder"/>.</param>
/// <param name="Ticked">Whether the owner ticked it for replacement.</param>
/// <param name="Status">Whether a tick here counts, and why not.</param>
/// <param name="RenameProblems">MAM-26 — for an <see cref="ReplacementFolderStatus.Available"/> folder, what keeps a
/// rename into the quarantine from working there (ADR 0033's hint), none when it can; <c>null</c> for any other
/// folder.</param>
/// <param name="WriteAccess">MAM-39 — when <paramref name="RenameProblems"/> holds <see cref="RenamePlaceProblem.NotWritable"/>,
/// which folder the app's user may not write to, whose it is and whom the app runs as; <c>null</c> otherwise, and when the
/// app couldn't read them.</param>
public sealed record ReplacementFolderDto(
    string AppPath,
    IReadOnlyList<string> LibraryNames,
    bool Ticked,
    ReplacementFolderStatus Status,
    IReadOnlyList<RenamePlaceProblem>? RenameProblems = null,
    ReplacementFolderWriteAccessDto? WriteAccess = null);
