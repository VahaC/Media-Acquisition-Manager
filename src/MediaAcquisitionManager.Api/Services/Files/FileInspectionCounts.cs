using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Where the app's look at a set of file names stands, counted from the names when asked, so it moves as
/// an inspection writes them (MAM-9). One rule for a library's row on the Library page and for the Media
/// folders row (MAM-18), so the two count alike.
/// </summary>
public static class FileInspectionCounts
{
    /// <summary>
    /// The counts of <paramref name="files"/> — each name's last look, its sentence, and whether the
    /// current path mappings give it an app path — given in path order, so "the first problem" is the
    /// same one on every poll.
    /// </summary>
    /// <remarks>
    /// MAM-9 — the files it read, those it couldn't (whatever the reason, a missing app path included),
    /// and those it hasn't looked at; the first problem's sentence, in path order, to the owner only, like
    /// each file's (<c>LibraryMapper</c>). A file the last run found no app path for, which the mappings
    /// give one now, waits for the next run, the works list's rule: a fixed mapping shows at once, as the
    /// path warning does.
    /// </remarks>
    public static LibrarySourceInspectionDto Of(
        IReadOnlyList<(FileInspectionStatus? Status, string? Error, bool HasAppPath)> files, bool forOwner)
    {
        var looks = files
            .Where(file => !(file.Status == FileInspectionStatus.NoAppPath && file.HasAppPath))
            .ToList();
        var problems = looks
            .Where(file => IsProblem(file.Status, file.HasAppPath))
            .ToList();
        return new LibrarySourceInspectionDto(
            looks.Count(file => file.Status == FileInspectionStatus.Inspected),
            problems.Count,
            files.Count - looks.Count + looks.Count(file => file.Status is null),
            forOwner && problems.Count > 0 ? problems[0].Error : null);
    }

    /// <summary>
    /// Whether a name's last look counts as "couldn't be read": it looked, didn't read the streams, and — for a name it
    /// found no app path for — the mappings still give it none. MAM-42 — the Files in no library list's Problems tab
    /// asks the same, so its count is the row's.
    /// </summary>
    public static bool IsProblem(FileInspectionStatus? status, bool hasAppPath) =>
        status is { } look && look != FileInspectionStatus.Inspected && !(look == FileInspectionStatus.NoAppPath && hasAppPath);
}
