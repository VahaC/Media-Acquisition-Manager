using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-25 — the first half of the check of a replacement's new files (ADR 0035): what the download client says of them,
/// read fresh (check 1), where each is inside the app and which library folder holds it, the app's own look at each
/// (check 2), and whether another of the client's downloads writes one of them (ADR 0034's gap, closed here).
/// </summary>
/// <remarks>
/// <para>
/// The client is asked again, never trusted from the stored rows: the poller stops reading a download once it is done,
/// so an error, a missing file or a re-check the client reports later would never be seen. A client that can't be asked
/// now — switched off, unreachable, a secret the app can't open — is a wait, never a pass.
/// </para>
/// <para>
/// Reads only: the client is listed and nothing else, and the disk is only looked at, one file at a time (the media
/// inspector shares the disk with the library's own reads). Never throws but for the caller's cancellation.
/// </para>
/// </remarks>
public interface IReplacementNewFileReader
{
    /// <summary>
    /// Checks the new files of <paramref name="download"/>, the done download of the replacement
    /// <paramref name="replacementJobId"/> holding <paramref name="tickedCopies"/>, at <paramref name="checkedUtc"/>; with
    /// the evidence of every new video file, as rows of that replacement.
    /// </summary>
    Task<ReplacementFilesCheck> CheckAsync(
        Guid replacementJobId,
        DownloadJobEntity download,
        IReadOnlyList<ReplacementJobFileEntity> tickedCopies,
        DateTime checkedUtc,
        CancellationToken cancellationToken = default);
}
