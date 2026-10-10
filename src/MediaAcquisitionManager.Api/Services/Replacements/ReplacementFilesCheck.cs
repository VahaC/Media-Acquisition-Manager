using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — checks 1 and 2 of a replacement's new files, and whether another download writes one of them
/// (<see cref="IReplacementNewFileReader"/>, ADR 0035).
/// </summary>
/// <param name="Status">Passed, not yet, the owner's review, or a bad file.</param>
/// <param name="Reason">
/// One readable sentence saying why not: the job's reason when it stops, and — for <see cref="ReplacementCheckStatus.Wait"/>
/// — the one it stops with once the files' wait ran out; <c>null</c> only for <see cref="ReplacementCheckStatus.Pass"/>.
/// </param>
/// <param name="NewFiles">
/// The evidence: one unsaved row per video file the client downloads (or can't say it doesn't), in the client's order,
/// with what each check found of it and why it stopped the job; empty when the client couldn't be read so far. They
/// replace the job's rows whole.
/// </param>
public sealed record ReplacementFilesCheck(ReplacementCheckStatus Status, string? Reason, IReadOnlyList<ReplacementJobNewFileEntity> NewFiles);
