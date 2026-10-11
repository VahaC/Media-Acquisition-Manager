namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// The sentences <see cref="LocalFileQuarantine"/> answers with (MAM-26): one readable English sentence each, every
/// path quoted with “ ” as <see cref="FileInventoryMessages"/> quotes it. The owner reads them on the replacement job
/// — after "“Alien.mkv” couldn't be moved into the quarantine:" — so each says why in the words of the app, not an
/// operating system's error code, and none says what happened next: every refusal left the name where it was, but for
/// <see cref="Swapped"/>'s, a rename that moved another file.
/// </summary>
internal static class FileQuarantineMessages
{
    /// <summary>The sentence of every write anywhere but Linux.</summary>
    public const string NotLinux =
        "Only the app running on Linux renames or deletes a library file, as only there can a rename be made without ever replacing or copying one.";

    public static string NotInQuarantine(string path) =>
        $"{Quote(path)} isn't inside the quarantine at the top of a media folder.";

    public static string AlreadyInQuarantine(string appPath) => $"{Quote(appPath)} already lies in a quarantine.";

    /// <param name="appPath">The name to move or put back.</param>
    /// <param name="quarantineFolder">The quarantine it was asked to go into or come out of.</param>
    public static string NotBesideQuarantine(string appPath, string quarantineFolder) =>
        $"{Quote(appPath)} isn't a file inside the media folder that holds the quarantine {Quote(quarantineFolder)}.";

    public static string NotFound(string appPath) => $"There is no file at {Quote(appPath)}.";

    public static string FolderGone(string folderAppPath) =>
        $"The folder {Quote(folderAppPath)} is gone, and the app creates no folder to put a file back.";

    public static string Changed(string appPath) =>
        $"{Quote(appPath)} changed since the app checked it: its size, modification time, device or inode isn't what was read then.";

    public static string NotARegularFile(string appPath) => $"{Quote(appPath)} isn't a regular file.";

    public static string ChangedWhileChecking(string appPath) =>
        $"{Quote(appPath)} changed while the app was checking it: a folder on the way or the file itself became a link or something else.";

    public static string NoIdentity(string appPath) =>
        $"The file system holding {Quote(appPath)} doesn't report a file's inode, so the app can't make sure it changes the file it checked.";

    public static string NoMountId(string appPath) =>
        $"This system doesn't say which mount {Quote(appPath)} is on (Linux 5.8 or later does), so the app can't make sure a rename stays on one mount.";

    public static string OtherMount(string appPath, string otherAppPath) =>
        $"{Quote(appPath)} and {Quote(otherAppPath)} are on different mounts, where a rename would be a copy.";

    public static string NameTaken(string appPath) => $"{Quote(appPath)} already holds a file, and the app never writes over one.";

    public static string ReadOnly(string appPath) => $"{Quote(appPath)} is on a read-only mount.";

    public static string NotAllowed(string appPath) =>
        $"The app's user isn't allowed to change {Quote(appPath)}: renaming or deleting a file needs write and search permission on its folder, and on the one it goes into.";

    public static string RenameUnsupported(string appPath) =>
        $"The file system holding {Quote(appPath)} can't rename a file without replacing a name already there, so the app renames nothing on it.";

    /// <summary>The name showed another device or inode right after the rename: a file put at it just before.</summary>
    public static string Swapped(string fromAppPath, string toAppPath) =>
        $"What the app renamed from {Quote(fromAppPath)} to {Quote(toAppPath)} isn't the file it checked: another file was put at that name just before the rename.";

    /// <param name="appPath">The name the call was about.</param>
    /// <param name="reason">The operating system's words and number: "Device or resource busy (errno 16)".</param>
    public static string CouldNotChange(string appPath, string reason) =>
        $"The app couldn't rename or delete {Quote(appPath)}: {Reason(reason)}.";

    /// <param name="appPath">The name or folder read.</param>
    /// <param name="reason">The operating system's words and number.</param>
    public static string CouldNotRead(string appPath, string reason) => FileInventoryMessages.CouldNotRead(appPath, reason);

    private static string Quote(string text) => FileInventoryMessages.Quote(text);

    private static string Reason(string text) => FileInventoryMessages.Reason(text);
}
