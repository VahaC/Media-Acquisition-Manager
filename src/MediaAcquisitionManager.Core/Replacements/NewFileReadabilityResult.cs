using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — check 2 of one new file of a replacement: the app's own look at it (ADR 0035).</summary>
/// <param name="Status">Readable, not yet, the owner must look, or not a usable media file.</param>
/// <param name="Problem">Why not, or not yet, as a message the Api renders (ADR 0042); <c>null</c> only for
/// <see cref="NewFileReadabilityStatus.Readable"/>.</param>
public sealed record NewFileReadabilityResult(NewFileReadabilityStatus Status, Message? Problem)
{
    /// <summary>The file passed the look.</summary>
    public static NewFileReadabilityResult Readable { get; } = new(NewFileReadabilityStatus.Readable, null);
}
