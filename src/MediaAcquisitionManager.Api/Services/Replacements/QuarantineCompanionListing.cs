using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — the companions a video takes with it into the quarantine (ADR 0030 "What"), as a replacement's mover (MAM-26)
/// and the owner's Remove both pick them: its folder listed through the write contract within the root whose quarantine
/// takes it, a video that is a link refused, and each of its own companions (<see cref="QuarantineCompanions.Pick"/>)
/// with the identity a torrent's hold on it is confirmed by.
/// </summary>
/// <remarks>Reads only; the sentences are the ones a stop names the folder or the file by — MAM-47 (ADR 0042) — as
/// messages of the <c>replacements.quarantine.companions</c> keys.</remarks>
internal static class QuarantineCompanionListing
{
    /// <summary>
    /// The companions of the video at <paramref name="appPath"/>, in its folder's order, each with what the listing read
    /// of it — or why nothing of the video may move: its folder couldn't be listed, the video's own name is a link, or a
    /// companion couldn't be read well enough.
    /// </summary>
    public static async Task<(IReadOnlyList<(NormalizedPath AppPath, FileFacts Facts)> Companions, Message? Reason)> ListAsync(
        IFileQuarantine quarantine, NormalizedPath appPath, string rootText, CancellationToken cancellationToken)
    {
        // Within the copy's own root, as its look was.
        var listed = await quarantine.ListFolderFilesAsync(appPath.ToString(), [rootText], cancellationToken);
        if (listed is not { Status: FileQuarantineStatus.Done, Value: { } folderFiles })
            // The listing's own sentence: it names the folder and why.
            return ([], listed.Error ?? NotListedReason(appPath.ToString()));

        // The listing leaves a link out: a video whose own name isn't among its folder's files is a link, which the look
        // followed to the file it leads to. Refused here, before the claim, rather than by the rename itself.
        if (!folderFiles.Any(file => NormalizedPath.TryParse(file.AppPath, out var listedPath, out _) && listedPath.IsSameAs(appPath)))
            return ([], LinkReason(appPath.ToString()));

        var companions = new List<(NormalizedPath AppPath, FileFacts Facts)>();
        foreach (var name in QuarantineCompanions.Pick(appPath.Segments[^1], [.. folderFiles.Select(file => file.Name)], appPath.Style))
        {
            var companion = folderFiles.First(file => string.Equals(file.Name, name, StringComparison.Ordinal));
            // Without its identity a torrent's hold on it can't be confirmed: the reader would pass it over as held by
            // none, and a held companion would move.
            if (companion.Facts is not { DeviceId: not null, Inode: not null } || !NormalizedPath.TryParse(companion.AppPath, out var path, out _))
                return ([], CompanionUnreadableReason(name));
            companions.Add((path, companion.Facts));
        }

        return (companions, null);
    }

    // The contract gives a sentence with every refusal; this is for one that broke it.
    private static Message NotListedReason(string path) =>
        Message.Of("replacements.quarantine.companions.not-listed", ("path", MessageText.Clean(path)));

    // The video's own name is a link, which the listing of its folder leaves out: renaming it would leave the file where it
    // is, and the file it leads to may be anyone's.
    private static Message LinkReason(string path) =>
        Message.Of("replacements.quarantine.companions.link", ("file", MessageText.Clean(ReplacementText.FileNameOf(path))));

    private static Message CompanionUnreadableReason(string name) =>
        Message.Of("replacements.quarantine.companions.unreadable", ("name", MessageText.Clean(name)));
}
