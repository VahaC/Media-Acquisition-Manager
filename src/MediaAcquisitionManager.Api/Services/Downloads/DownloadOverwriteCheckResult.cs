using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>MAM-38 — the answer to the Download dialog's question about a destination's folder, or why there is
/// none.</summary>
/// <param name="Status">How the question went.</param>
/// <param name="Answer">The answer; <c>null</c> unless <see cref="DownloadOverwriteCheckStatus.Answered"/>.</param>
/// <param name="Error">Why the check wasn't answered, as a message the Api renders (ADR 0042); <c>null</c> when
/// answered.</param>
public sealed record DownloadOverwriteCheckResult(DownloadOverwriteCheckStatus Status, DownloadOverwriteCheckResponse? Answer, Message? Error)
{
    public static DownloadOverwriteCheckResult Answered(DownloadOverwriteCheckResponse answer) => new(DownloadOverwriteCheckStatus.Answered, answer, null);

    public static DownloadOverwriteCheckResult Invalid(Message error) => new(DownloadOverwriteCheckStatus.Invalid, null, error);

    public static DownloadOverwriteCheckResult DestinationMissing(Message error) => new(DownloadOverwriteCheckStatus.DestinationMissing, null, error);
}
