using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The sentences <see cref="LocalFileQuarantine"/> answers with (MAM-26): one message each, every path quoted with “ ” as
/// <see cref="FileInventoryMessages"/> quotes it. The owner reads them on the replacement job — after "“Alien.mkv”
/// couldn't be moved into the quarantine:" — so each says why in the words of the app, not an operating system's error
/// code, and none says what happened next: every refusal left the name where it was, but for <see cref="Swapped"/>'s, a
/// rename that moved another file.
/// </summary>
/// <remarks>MAM-47 (ADR 0042) — each is a key of <c>files.json</c> (<c>files.quarantine.*</c>, or the inventory's own
/// where the words are the same) with its values; the Api writes it in the reader's language.</remarks>
internal static class FileQuarantineMessages
{
    /// <summary>The sentence of every write anywhere but Linux.</summary>
    public static readonly Message NotLinux = Message.Of("files.quarantine.not-linux");

    public static Message NotInQuarantine(string path) =>
        Message.Of("files.quarantine.not-in-quarantine", ("path", Path(path)));

    public static Message AlreadyInQuarantine(string appPath) =>
        Message.Of("files.quarantine.already-in-quarantine", ("path", Path(appPath)));

    /// <param name="appPath">The name to move or put back.</param>
    /// <param name="quarantineFolder">The quarantine it was asked to go into or come out of.</param>
    public static Message NotBesideQuarantine(string appPath, string quarantineFolder) =>
        Message.Of("files.quarantine.not-beside-quarantine", ("path", Path(appPath)), ("quarantine", Path(quarantineFolder)));

    /// <summary>The inventory's own words: "There is no file at “…”."</summary>
    public static Message NotFound(string appPath) => FileInventoryMessages.NotFound(appPath);

    public static Message FolderGone(string folderAppPath) =>
        Message.Of("files.quarantine.folder-gone", ("folder", Path(folderAppPath)));

    public static Message Changed(string appPath) => Message.Of("files.quarantine.changed", ("path", Path(appPath)));

    public static Message NotARegularFile(string appPath) => Message.Of("files.quarantine.not-a-regular-file", ("path", Path(appPath)));

    public static Message ChangedWhileChecking(string appPath) =>
        Message.Of("files.quarantine.changed-while-checking", ("path", Path(appPath)));

    public static Message NoIdentity(string appPath) => Message.Of("files.quarantine.no-identity", ("path", Path(appPath)));

    public static Message NoMountId(string appPath) => Message.Of("files.quarantine.no-mount-id", ("path", Path(appPath)));

    public static Message OtherMount(string appPath, string otherAppPath) =>
        Message.Of("files.quarantine.other-mount", ("path", Path(appPath)), ("other", Path(otherAppPath)));

    public static Message NameTaken(string appPath) => Message.Of("files.quarantine.name-taken", ("path", Path(appPath)));

    public static Message ReadOnly(string appPath) => Message.Of("files.quarantine.read-only", ("path", Path(appPath)));

    public static Message NotAllowed(string appPath) => Message.Of("files.quarantine.not-allowed", ("path", Path(appPath)));

    public static Message RenameUnsupported(string appPath) =>
        Message.Of("files.quarantine.rename-unsupported", ("path", Path(appPath)));

    /// <summary>The name showed another device or inode right after the rename: a file put at it just before.</summary>
    public static Message Swapped(string fromAppPath, string toAppPath) =>
        Message.Of("files.quarantine.swapped", ("from", Path(fromAppPath)), ("to", Path(toAppPath)));

    /// <param name="appPath">The name the call was about.</param>
    /// <param name="reason">The operating system's words and number: "Device or resource busy (errno 16)".</param>
    public static Message CouldNotChange(string appPath, string reason) =>
        Message.Of("files.quarantine.could-not-change", ("path", Path(appPath)), ("reason", FileInventoryMessages.Reason(reason)));

    /// <param name="appPath">The name or folder read.</param>
    /// <param name="reason">The operating system's words and number.</param>
    public static Message CouldNotRead(string appPath, string reason) => FileInventoryMessages.CouldNotRead(appPath, reason);

    /// <summary>Linux reported a modification time a <see cref="DateTime"/> can't hold: the inventory's own words.</summary>
    public static Message TimeOutOfRange(string appPath) => FileInventoryMessages.TimeOutOfRange(appPath);

    private static string Path(string text) => FileInventoryMessages.Path(text);
}
