using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-26 — what an owner's Retry, Restore or Delete for good of a replacement answered.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Job">The download job with its replacement line as it stands now, when
/// <see cref="ReplacementActionStatus.Done"/>.</param>
/// <param name="Error">Why not, as a message the controller renders (MAM-47), when <see cref="ReplacementActionStatus.Invalid"/> or
/// <see cref="ReplacementActionStatus.Refused"/>.</param>
public sealed record ReplacementActionResult(ReplacementActionStatus Status, DownloadJobDto? Job, Message? Error)
{
    public static ReplacementActionResult NotFound { get; } = new(ReplacementActionStatus.NotFound, null, null);

    public static ReplacementActionResult Done(DownloadJobDto job) => new(ReplacementActionStatus.Done, job, null);

    public static ReplacementActionResult Invalid(Message error) => new(ReplacementActionStatus.Invalid, null, error);

    public static ReplacementActionResult Refused(Message error) => new(ReplacementActionStatus.Refused, null, error);
}
