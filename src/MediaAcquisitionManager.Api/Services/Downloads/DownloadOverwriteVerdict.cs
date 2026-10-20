using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Downloads;

/// <summary>MAM-38 — what the overwrite check said about one download before it was sent.</summary>
/// <param name="Check">How it was checked, recorded on the job, when it may be sent; <c>null</c> when it is
/// refused.</param>
/// <param name="Refusal">Why it is refused — the app's own sentence, naming the file it would write over, as a message
/// (ADR 0042) —; <c>null</c> when it may be sent.</param>
public sealed record DownloadOverwriteVerdict(DownloadOverwriteCheck? Check, Message? Refusal)
{
    /// <summary>May be sent, checked or saying why it wasn't.</summary>
    public static DownloadOverwriteVerdict Send(DownloadOverwriteCheck check) => new(check, null);

    /// <summary>Must not be sent, with the sentence why.</summary>
    public static DownloadOverwriteVerdict Refuse(Message refusal) => new(null, refusal);
}
