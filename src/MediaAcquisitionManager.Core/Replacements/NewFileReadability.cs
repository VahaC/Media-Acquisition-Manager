using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — check 2 of a replacement's new file (ADR 0035): the app's own look at it, through the file inventory and its
/// media inspector, inside the allowed folders. Pure: the verifier reads the file and stores what was found.
/// </summary>
/// <remarks>
/// <para>
/// Failed only for a bad file — not a regular file, unreadable as media, empty, or without a video stream or a running
/// time: no wait or review makes such a file a copy (Vasyl's default, 2026-10-10). Everything else that stops is the
/// owner's review: a file missing, outside the allowed folders or unreadable to the app may come right, and a file of
/// another size than the client downloaded, or with no identity to tell it from the old copies, may be another file.
/// </para>
/// <para>
/// The new file must not be one of the old copies, by identity or by path: MAM-24's check before the send refused a
/// download that would write over a file, but a torrent of another category may still write there (ADR 0034's gap), and
/// a copy replaced by itself is no replacement.
/// </para>
/// </remarks>
public static class NewFileReadability
{
    /// <summary>
    /// What <paramref name="inspection"/>, the inventory's look at the new file at <paramref name="appPath"/> (canonical),
    /// says, the client <paramref name="clientName"/> having reported <paramref name="clientSizeBytes"/> for it
    /// (<c>null</c> when it didn't say), against the identities of the copies the job holds,
    /// <paramref name="tickedCopies"/>. The first that applies: the inventory's own failure, an identity the platform
    /// doesn't give, an empty file, no video stream, no running time, another size than the client's, the old copy
    /// itself.
    /// </summary>
    public static NewFileReadabilityResult Check(
        string appPath,
        FileInventoryResult<MediaInspection> inspection,
        long? clientSizeBytes,
        IReadOnlyList<TickedCopyIdentity> tickedCopies,
        string clientName)
    {
        ArgumentNullException.ThrowIfNull(appPath);
        ArgumentNullException.ThrowIfNull(inspection);
        ArgumentNullException.ThrowIfNull(tickedCopies);
        ArgumentNullException.ThrowIfNull(clientName);

        var path = ReplacementText.Quote(appPath);
        var said = inspection.Error ?? $"The app couldn't read {path}.";
        if (inspection is not { Status: FileInventoryStatus.Found, Value: { } found })
        {
            return inspection.Status switch
            {
                // The file may still come right: a mount back, a permission given, the folder allowed.
                FileInventoryStatus.NotFound or FileInventoryStatus.OutsideAllowedRoots or FileInventoryStatus.Unreadable =>
                    new(NewFileReadabilityStatus.Review, said),
                // A fact of this moment or of the app's host, not of the file: read again next pass.
                FileInventoryStatus.ChangedWhileReading or FileInventoryStatus.InspectorUnavailable => new(NewFileReadabilityStatus.Wait, said),
                // A bad file.
                FileInventoryStatus.MediaUnreadable or FileInventoryStatus.NotARegularFile => new(NewFileReadabilityStatus.Failed, said),
                // A status this build doesn't know, or a find with nothing read: never a pass, and never a bad file by a guess.
                _ => new(NewFileReadabilityStatus.Review, said),
            };
        }

        var file = found.File;
        if (file.DeviceId is not { } deviceId || file.Inode is not { } inode)
            return new(
                NewFileReadabilityStatus.Review,
                $"The app can't read the identity of {path} on this platform, so it can't tell the new file from the old copies.");
        if (file.SizeBytes == 0)
            return new(NewFileReadabilityStatus.Failed, $"{path} is empty.");
        if (!found.Media.Tracks.Any(track => track.Kind == MediaTrackKind.Video))
            return new(NewFileReadabilityStatus.Failed, $"{path} has no video stream.");
        if (found.Media.Duration is not { } duration || duration <= TimeSpan.Zero)
            return new(NewFileReadabilityStatus.Failed, $"The media inspector found no running time in {path}.");
        if (clientSizeBytes is { } downloaded && downloaded != file.SizeBytes)
        {
            var client = ReplacementText.Quote(clientName);
            return new(
                NewFileReadabilityStatus.Review,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{path} is {file.SizeBytes:N0} bytes on the disk, not the {downloaded:N0} bytes {client} downloaded, so it may be another file."));
        }

        var isOldCopy = tickedCopies.Any(copy => (copy.DeviceId == deviceId && copy.Inode == inode) || SamePath(copy.AppPath, appPath));
        return isOldCopy ? new(NewFileReadabilityStatus.Review, $"The new file {path} is the old copy itself.") : NewFileReadabilityResult.Readable;
    }

    /// <summary>
    /// Whether two canonical app paths name one place, under their style's rules (a Windows path ignores letter case);
    /// two texts that don't parse compare as they are.
    /// </summary>
    private static bool SamePath(string first, string second) =>
        NormalizedPath.TryParse(first, out var one, out _) && NormalizedPath.TryParse(second, out var other, out _)
            ? one.IsSameAs(other)
            : string.Equals(first, second, StringComparison.Ordinal);
}
