using System.Globalization;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Infrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The sentences <see cref="LocalFileInventory"/> answers with (MAM-9), its walk of the allowed roots included
/// (MAM-18): one readable English sentence each, every path quoted with “ ”. The owner reads them on the
/// Library page, so they say what happened to the file in the words of the app, not an operating system's
/// error code.
/// </summary>
internal static class FileInventoryMessages
{
    /// <summary>The sentence when no allowed root is usable.</summary>
    public const string NoAllowedRoots = "No media folder is allowed yet, so the app reads no file.";

    /// <summary>
    /// A path can be 4096 characters long, and the sentence is stored in a column of 1000
    /// (<c>MediaInspectionEntity.ErrorMaximumLength</c>): quoted whole, a deep path would push the reason off
    /// the end. The start of a path says where it is; its end is in the file's row beside the sentence.
    /// </summary>
    private const int MaximumQuotedPathLength = 240;

    /// <summary>An operating system's or ffprobe's explanation; theirs are a line long.</summary>
    private const int MaximumQuotedReasonLength = 300;

    public static string NotAFullPath(string appPath) => $"{Quote(appPath)} is not a full path, so the app doesn't read it.";

    public static string DotSegment(string appPath) =>
        $"{Quote(appPath)} contains . or .. as a folder name, so the app doesn't read it.";

    public static string OutsideAllowedRoots(string appPath) =>
        $"{Quote(appPath)} is outside the media folders the app may read.";

    public static string LinkLeadsOut(string appPath, string resolvedPath) =>
        $"{Quote(appPath)} leads through a link to {Quote(resolvedPath)}, outside the media folders the app may read.";

    public static string UnfollowableLink(string appPath, string target) =>
        $"{Quote(appPath)} leads through a link to {Quote(target)}, which the app doesn't follow.";

    public static string TooManyLinks(string appPath) =>
        $"{Quote(appPath)} leads through more than {LinkResolver.MaximumLinkHops} links, likely a loop, so the app doesn't follow them.";

    public static string NotFound(string appPath) => $"There is no file at {Quote(appPath)}.";

    public static string Folder(string appPath) => $"{Quote(appPath)} is a folder, not a file.";

    public static string NotARegularFile(string appPath) =>
        $"{Quote(appPath)} is not a regular file but a device, a socket or a pipe.";

    public static string NotAllowedToRead(string appPath) => $"The app isn't allowed to read {Quote(appPath)}.";

    public static string CouldNotRead(string appPath, string reason) =>
        $"The app couldn't read {Quote(appPath)}: {Reason(reason)}.";

    public static string ChangedWhileChecking(string appPath) =>
        $"{Quote(appPath)} changed while the app was checking it: a folder on the way or the file itself became a link or something else, so the app didn't open it.";

    public static string ChangedWhileReading(string appPath) =>
        $"{Quote(appPath)} changed while ffprobe was reading it, so what was read can't be said to belong to either version.";

    /// <summary>MAM-19 — the file changed while the app read it whole for its hash.</summary>
    public static string ChangedWhileHashing(string appPath) =>
        $"{Quote(appPath)} changed while the app was reading it for its hash, so the hash can't be said to belong to either version.";

    /// <param name="appPath">The file.</param>
    /// <param name="reason">A clause from <see cref="MediaProbeResult.Reason"/>: "it said “…”", "it didn't
    /// finish within 2 minutes".</param>
    public static string MediaUnreadable(string appPath, string reason) =>
        $"ffprobe couldn't read {Quote(appPath)} as media: {reason}.";

    public static string InspectorUnavailable(string reason) =>
        $"The app couldn't start ffprobe ({Reason(reason)}); the Docker image includes it, and elsewhere FFmpeg " +
        "must be installed with ffprobe on the PATH.";

    /// <summary>
    /// MAM-18 — an allowed root that lists nothing at all: an unmounted disk looks exactly like this, so it is
    /// a root the walk couldn't list, never a folder found empty.
    /// </summary>
    public static string EmptyRoot(string appPath) =>
        $"{Quote(appPath)} is empty; if it is a mount, it may be away, so the files found in it before are kept.";

    /// <param name="folderAppPath">The folder the name is in: the name itself can't be quoted.</param>
    public static string UnusableName(string folderAppPath) =>
        $"A name in {Quote(folderAppPath)} isn't valid text, so the app skips it.";

    public static string PathTooLong(string appPath) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Quote(appPath)} is longer than {PhysicalFileEntity.ServicePathMaximumLength} characters, so the app skips it and everything in it.");

    public static string LeadsBackUp(string appPath) =>
        $"{Quote(appPath)} leads back to a folder above it, so the app doesn't walk it again.";

    /// <param name="maximumEntries">The entries one walk gives at most (<see cref="FolderWalker.MaximumEntries"/>).</param>
    /// <param name="maximumFolders">The folders one walk lists at most (<see cref="FolderWalker.MaximumFolders"/>).</param>
    public static string LimitReached(int maximumEntries, int maximumFolders) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"The media folders hold more than {maximumEntries:N0} media files or {maximumFolders:N0} folders, or links inside them lead to the same folders many times over, so the scan stopped.");

    /// <summary>
    /// <paramref name="text"/> in “ ”, made safe to show: a path is whatever a service reported or a file
    /// was named, and a bidi override or a line break in it would garble the sentence or the log line
    /// (<see cref="RemoteText"/>).
    /// </summary>
    public static string Quote(string text) => $"“{RemoteText.Clean(text, MaximumQuotedPathLength, [])}”";

    /// <summary>
    /// An explanation the operating system or ffprobe gave, cleaned like <see cref="Quote"/> and without its
    /// own full stop, so it can sit inside a sentence.
    /// </summary>
    public static string Reason(string text) => RemoteText.Clean(text, MaximumQuotedReasonLength, []).TrimEnd('.');
}
