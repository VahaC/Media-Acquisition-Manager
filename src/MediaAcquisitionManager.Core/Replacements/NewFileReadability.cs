using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

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
/// <para>
/// MAM-47 (ADR 0042) — each reason is a message the Api renders; the inventory's own message is passed on as it is.
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

        var path = MessageText.Clean(appPath);
        var said = inspection.Error ?? Message.Of("replacements.readability.unread", ("path", path));
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
            return new(NewFileReadabilityStatus.Review, Message.Of("replacements.readability.identity-unknown", ("path", path)));
        if (file.SizeBytes == 0)
            return new(NewFileReadabilityStatus.Failed, Message.Of("replacements.readability.empty", ("path", path)));
        if (!found.Media.Tracks.Any(track => track.Kind == MediaTrackKind.Video))
            return new(NewFileReadabilityStatus.Failed, Message.Of("replacements.readability.no-video", ("path", path)));
        if (found.Media.Duration is not { } duration || duration <= TimeSpan.Zero)
            return new(NewFileReadabilityStatus.Failed, Message.Of("replacements.readability.no-running-time", ("path", path)));
        if (clientSizeBytes is { } downloaded && downloaded != file.SizeBytes)
        {
            // Exact byte counts, grouped as numbers (number:grouped), never sizes: 1,234,567 and 1,234,560 bytes would both
            // read 1.18 MiB, and the reason would be lost.
            return new(
                NewFileReadabilityStatus.Review,
                Message.Of(
                    "replacements.readability.size-differs",
                    ("path", path),
                    ("size", Bytes(file.SizeBytes)),
                    ("downloaded", Bytes(downloaded)),
                    ("client", MessageText.Clean(clientName))));
        }

        var isOldCopy = tickedCopies.Any(copy => (copy.DeviceId == deviceId && copy.Inode == inode) || SamePath(copy.AppPath, appPath));
        return isOldCopy
            ? new(NewFileReadabilityStatus.Review, Message.Of("replacements.readability.old-copy-itself", ("path", path)))
            : NewFileReadabilityResult.Readable;
    }

    /// <summary>A count of bytes as the size reason gives it: its noun counted on its own, since a key has one plural
    /// driver and the reason counts two.</summary>
    private static Message Bytes(long count) => Message.Of("replacements.readability.tally.bytes", ("count", count));

    /// <summary>
    /// Whether two canonical app paths name one place, under their style's rules (a Windows path ignores letter case);
    /// two texts that don't parse compare as they are.
    /// </summary>
    private static bool SamePath(string first, string second) =>
        NormalizedPath.TryParse(first, out var one, out _) && NormalizedPath.TryParse(second, out var other, out _)
            ? one.IsSameAs(other)
            : string.Equals(first, second, StringComparison.Ordinal);
}
