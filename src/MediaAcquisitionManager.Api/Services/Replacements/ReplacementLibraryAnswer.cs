using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-25 — what one media library said when the check of a replacement's new files asked it
/// (<see cref="IReplacementLibraryReader"/>, ADR 0035).</summary>
/// <param name="Status">Answered, not now, or never through this library.</param>
/// <param name="LibraryName">The library integration's name, as the sentences quote it; its id when it is gone.</param>
/// <param name="Readings">What it holds at each path asked, in their order; empty unless a read was
/// <see cref="ReplacementCheckStatus.Pass"/>.</param>
/// <param name="Reason">
/// Why not, one readable sentence with the call's secrets masked, worded as the reason the job stops with — at once for
/// <see cref="ReplacementCheckStatus.Review"/>, once the wait ran out for <see cref="ReplacementCheckStatus.Wait"/>;
/// <c>null</c> for <see cref="ReplacementCheckStatus.Pass"/>.
/// </param>
public sealed record ReplacementLibraryAnswer(
    ReplacementCheckStatus Status,
    string LibraryName,
    IReadOnlyList<LibraryFileReading> Readings,
    string? Reason);
