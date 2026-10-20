using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The sentences <see cref="LocalFileInventory"/> answers with (MAM-9), its walk of the allowed roots included
/// (MAM-18): one message each, every path quoted with “ ” by its template. The owner reads them on the Library page, so
/// they say what happened to the file in the words of the app, not an operating system's error code.
/// </summary>
/// <remarks>
/// MAM-47 (ADR 0042) — each is a key of <c>files.json</c> (<c>files.inventory.*</c>) with its values; the Api writes it
/// in the reader's language. A path is cleaned by <see cref="RemoteText"/>'s rule (whitespace folded, cut at 240), not
/// <see cref="MessageText.Clean"/>'s, so the English stays what it was byte for byte (WP1a's note); an operating system's
/// or ffprobe's explanation is a <see cref="VendorText"/>.
/// </remarks>
internal static class FileInventoryMessages
{
    /// <summary>The sentence when no allowed root is usable.</summary>
    public static readonly Message NoAllowedRoots = Message.Of("files.inventory.no-allowed-roots");

    /// <summary>
    /// A path can be 4096 characters long, and the sentence is stored in a column of 1000
    /// (<c>MediaInspectionEntity.ErrorMaximumLength</c>): quoted whole, a deep path would push the reason off
    /// the end. The start of a path says where it is; its end is in the file's row beside the sentence.
    /// </summary>
    private const int MaximumQuotedPathLength = 240;

    /// <summary>An operating system's or ffprobe's explanation; theirs are a line long.</summary>
    private const int MaximumQuotedReasonLength = 300;

    public static Message NotAFullPath(string appPath) => Message.Of("files.inventory.not-full-path", ("path", Path(appPath)));

    public static Message DotSegment(string appPath) => Message.Of("files.inventory.dot-segment", ("path", Path(appPath)));

    public static Message OutsideAllowedRoots(string appPath) =>
        Message.Of("files.inventory.outside-allowed-roots", ("path", Path(appPath)));

    public static Message LinkLeadsOut(string appPath, string resolvedPath) =>
        Message.Of("files.inventory.link-leads-out", ("path", Path(appPath)), ("target", Path(resolvedPath)));

    public static Message UnfollowableLink(string appPath, string target) =>
        Message.Of("files.inventory.unfollowable-link", ("path", Path(appPath)), ("target", Path(target)));

    public static Message TooManyLinks(string appPath) =>
        Message.Of("files.inventory.too-many-links", ("path", Path(appPath)), ("count", LinkResolver.MaximumLinkHops));

    public static Message NotFound(string appPath) => Message.Of("files.inventory.not-found", ("path", Path(appPath)));

    public static Message Folder(string appPath) => Message.Of("files.inventory.folder", ("path", Path(appPath)));

    public static Message NotARegularFile(string appPath) => Message.Of("files.inventory.not-a-regular-file", ("path", Path(appPath)));

    public static Message NotAllowedToRead(string appPath) => Message.Of("files.inventory.not-allowed-to-read", ("path", Path(appPath)));

    /// <param name="appPath">The file or folder read.</param>
    /// <param name="reason">The operating system's or .NET's words: "Device or resource busy (errno 16)".</param>
    public static Message CouldNotRead(string appPath, string reason) =>
        Message.Of("files.inventory.could-not-read", ("path", Path(appPath)), ("reason", Reason(reason)));

    /// <summary>Linux reported a modification time a <see cref="DateTime"/> can't hold.</summary>
    public static Message TimeOutOfRange(string appPath) => Message.Of("files.inventory.time-out-of-range", ("path", Path(appPath)));

    /// <summary>Windows reported a modification time past what a <see cref="DateTime"/> holds.</summary>
    public static Message TimePastYear9999(string appPath) => Message.Of("files.inventory.time-past-9999", ("path", Path(appPath)));

    public static Message ChangedWhileChecking(string appPath) =>
        Message.Of("files.inventory.changed-while-checking", ("path", Path(appPath)));

    public static Message ChangedWhileReading(string appPath) =>
        Message.Of("files.inventory.changed-while-reading", ("path", Path(appPath)));

    /// <summary>MAM-19 — the file changed while the app read it whole for its hash.</summary>
    public static Message ChangedWhileHashing(string appPath) =>
        Message.Of("files.inventory.changed-while-hashing", ("path", Path(appPath)));

    /// <summary>MAM-38 — the file changed, or ended early, while the app read some of its pieces.</summary>
    public static Message ChangedWhileHashingPieces(string appPath) =>
        Message.Of("files.inventory.changed-while-hashing-pieces", ("path", Path(appPath)));

    /// <param name="appPath">The file.</param>
    /// <param name="reason">A clause from <see cref="MediaProbeResult.Reason"/> ("it said “…”", "it didn't finish within
    /// 2 minutes") or the inventory's own (<see cref="NoAnswer"/>, <see cref="NotJson"/>).</param>
    public static Message MediaUnreadable(string appPath, Message reason) =>
        Message.Of("files.inventory.media-unreadable", ("path", Path(appPath)), ("reason", reason));

    /// <summary>ffprobe ran and gave nothing at all: the clause for <see cref="MediaUnreadable"/>.</summary>
    public static readonly Message NoAnswer = Message.Of("files.ffprobe.no-answer-clause");

    /// <summary>ffprobe's answer didn't parse: the clause for <see cref="MediaUnreadable"/>.</summary>
    public static readonly Message NotJson = Message.Of("files.ffprobe.not-json-clause");

    /// <param name="reason">The operating system's words for why ffprobe couldn't start.</param>
    public static Message InspectorUnavailable(string reason) =>
        Message.Of("files.inventory.inspector-unavailable", ("reason", Reason(reason)));

    /// <summary>
    /// MAM-18 — an allowed root that lists nothing at all: an unmounted disk looks exactly like this, so it is
    /// a root the walk couldn't list, never a folder found empty.
    /// </summary>
    public static Message EmptyRoot(string appPath) => Message.Of("files.inventory.empty-root", ("path", Path(appPath)));

    /// <param name="folderAppPath">The folder the name is in: the name itself can't be quoted.</param>
    public static Message UnusableName(string folderAppPath) =>
        Message.Of("files.inventory.unusable-name", ("folder", Path(folderAppPath)));

    public static Message PathTooLong(string appPath) =>
        Message.Of("files.inventory.path-too-long", ("path", Path(appPath)), ("count", PhysicalFileEntity.ServicePathMaximumLength));

    public static Message LeadsBackUp(string appPath) => Message.Of("files.inventory.leads-back-up", ("path", Path(appPath)));

    /// <summary>MAM-47 — two counted nouns: the media files drive the sentence's plural, the folders are a nested
    /// tally with their own (design §2.2). Both grouped as the <c>N0</c> they were written with.</summary>
    /// <param name="maximumEntries">The entries one walk gives at most (<see cref="FolderWalker.MaximumEntries"/>).</param>
    /// <param name="maximumFolders">The folders one walk lists at most (<see cref="FolderWalker.MaximumFolders"/>).</param>
    public static Message LimitReached(int maximumEntries, int maximumFolders) =>
        Message.Of(
            "files.inventory.limit-reached",
            ("count", maximumEntries),
            ("folders", Message.Of("files.inventory.tally.folders", ("count", maximumFolders))));

    /// <summary>
    /// <paramref name="text"/> made safe to show inside the template's quotes: a path is whatever a service reported or a
    /// file was named, and a bidi override or a line break in it would garble the sentence or the log line
    /// (<see cref="RemoteText"/>).
    /// </summary>
    public static string Path(string text) => RemoteText.Clean(text, MaximumQuotedPathLength, []);

    /// <summary>
    /// An explanation the operating system or ffprobe gave, cleaned like <see cref="Path"/> and without its own full
    /// stop, so it can sit inside a sentence.
    /// </summary>
    /// <remarks>MAM-47 — cleaned again after the full stop goes, as a <see cref="VendorText"/> is only ever made by
    /// cleaning: the same words unless they ended in a space before the stop (which the second cleaning trims).</remarks>
    public static VendorText Reason(string text) =>
        VendorText.Clean(RemoteText.Clean(text, MaximumQuotedReasonLength, []).TrimEnd('.'), MaximumQuotedReasonLength);
}
