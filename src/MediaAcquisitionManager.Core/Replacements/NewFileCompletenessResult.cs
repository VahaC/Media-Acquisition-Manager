using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>MAM-25 — check 1 of a replacement's new files: what the download client's word says (ADR 0035).</summary>
/// <param name="Status">Complete, not yet, or the owner must look.</param>
/// <param name="NewFiles">The new video files the client downloads, in its order; empty unless
/// <see cref="NewFileCompletenessStatus.Complete"/> came of a look at the files.</param>
/// <param name="Problem">Why not, or not yet, as a message the Api renders (ADR 0042); <c>null</c> only for
/// <see cref="NewFileCompletenessStatus.Complete"/>.</param>
public sealed record NewFileCompletenessResult(NewFileCompletenessStatus Status, IReadOnlyList<ClientNewFile> NewFiles, Message? Problem)
{
    /// <summary>The client's job is finished; its files are still to be looked at.</summary>
    public static NewFileCompletenessResult Finished { get; } = new(NewFileCompletenessStatus.Complete, [], null);

    /// <summary>Not yet, for the reason <paramref name="problem"/> gives.</summary>
    public static NewFileCompletenessResult Wait(Message problem) => new(NewFileCompletenessStatus.Wait, [], problem);

    /// <summary>The owner must look, for the reason <paramref name="problem"/> gives.</summary>
    public static NewFileCompletenessResult Review(Message problem) => new(NewFileCompletenessStatus.Review, [], problem);
}
