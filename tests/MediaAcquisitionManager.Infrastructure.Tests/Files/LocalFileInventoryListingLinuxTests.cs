using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Listed = (MediaAcquisitionManager.Core.Abstractions.Files.FileListingStatus Status, string AppPath, string? ResolvedPath, string? Error);

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-18 — the walk where only Linux can show it (ADR 0021): a folder swapped for a link after the folder it is in
// was listed is refused, never listed through, because the reader opens folders as it opens files, following no link;
// names that aren't UTF-8 or hold a control character are skipped, never given with U+FFFD in them; a folder the app
// may not list is answered as unlisted while its neighbours are walked, and so is a link leading into it; and a pipe named like media is given without
// being opened. Linux only: this is where the app runs, and where CI runs the tests.
public sealed class LocalFileInventoryListingLinuxTests : IDisposable
{
    private const string LinuxOnly = "The Linux reader runs on Linux only; CI runs these tests there.";

    private readonly TemporaryFolder _folder = new("mam-file-listing-linux-");

    // The walk never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());

    private readonly List<string> _locked = [];
    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryListingLinuxTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    /// <summary>For <c>SkipUnless</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose()
    {
        // A folder with mode 000 can't be emptied: it gets its mode back first.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var folder in _locked)
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        _folder.Dispose();
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task List_answers_a_folder_swapped_for_a_link_after_its_folder_was_listed_as_unlisted()
    {
        //Arrange
        // The walk listed /media and found the folder "b"; before it opens "b", someone who may write in /media puts
        // a link to a folder outside in its place.
        _folder.WriteFile("1", "media", "a.mkv");
        _folder.WriteFile("2", "media", "b", "b.mkv");
        _folder.WriteFile("secret", "outside", "secret.mkv");
        var swapped = _folder.PathOf("media", "b");
        await using var walk = _inventory.ListMediaFilesAsync([_media], TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await walk.MoveNextAsync();
        var first = InEnglish(walk.Current);
        Directory.Move(swapped, _folder.PathOf("media", "b-aside"));
        FileLinks.CreateFolderLink(swapped, _outside);

        //Act
        var rest = new List<Listed>();
        while (await walk.MoveNextAsync())
            rest.Add(InEnglish(walk.Current));

        //Assert
        Assert.Equal(Media(_media, "a.mkv"), first);
        // Nothing from outside: the link is met while the folder is opened, and not followed.
        Assert.Equal(
            [
                Problem(
                    FileListingStatus.Unlisted,
                    swapped,
                    $"“{swapped}” changed while the app was checking it: a folder on the way or the file itself became a link or something else, so the app didn't open it."),
            ],
            rest);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task List_skips_names_that_are_not_utf8_and_never_walks_such_a_folder()
    {
        //Arrange
        // Read back with U+FFFD in place of the bad byte, such a name would be stored as another name than the disk's.
        using var names = new NonUtf8FileNames();
        var folder = NonUtf8FileNames.PathOf(_media, [.. "dir"u8, NonUtf8FileNames.InvalidByte]);
        names.CreateFolder(folder);
        names.CreateFile(NonUtf8FileNames.PathOf(folder, "inside.mkv"));
        names.CreateFile(NonUtf8FileNames.PathOf(_media, [.. "film"u8, NonUtf8FileNames.InvalidByte, .. ".mkv"u8]));
        _folder.WriteFile("1", "media", "ok.mkv");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        // The folder the names are in stands for them: neither can be quoted.
        var skipped = Problem(FileListingStatus.Skipped, _media, $"A name in “{_media}” isn't valid text, so the app skips it.");
        Assert.Equal([skipped, skipped, Media(_media, "ok.mkv")], entries);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task List_skips_names_with_control_characters_and_never_walks_such_a_folder()
    {
        //Arrange
        // Linux allows any character but / in a name, a line break too; quoted in a sentence or a log line, it would
        // break it.
        _folder.WriteFile("1", "media", "bad\nname.mkv");
        _folder.WriteFile("2", "media", "tab\there", "inside.mkv");
        _folder.WriteFile("3", "media", "ok.mkv");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        var skipped = Problem(FileListingStatus.Skipped, _media, $"A name in “{_media}” isn't valid text, so the app skips it.");
        Assert.Equal([skipped, Media(_media, "ok.mkv"), skipped], entries);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task List_answers_a_folder_the_app_may_not_list_as_unlisted_and_walks_its_neighbours()
    {
        //Arrange
        _folder.WriteFile("1", "media", "a.mkv");
        _folder.WriteFile("2", "media", "locked", "hidden.mkv");
        _folder.WriteFile("3", "media", "z", "z.mkv");
        var locked = _folder.PathOf("media", "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        _locked.Add(locked);
        if (CanList(locked))
            Assert.Skip("This process lists a folder with mode 000 anyway (it runs as root), so it can't see the refusal.");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        // Unlisted, not empty: what was found in it before is kept.
        Assert.Equal(
            [
                Media(_media, "a.mkv"),
                Problem(FileListingStatus.Unlisted, locked, $"The app isn't allowed to read “{locked}”."),
                Media(_media, "z", "z.mkv"),
            ],
            entries);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task List_answers_a_link_into_a_folder_the_app_may_not_look_into_as_unlisted_not_skipped()
    {
        //Arrange
        // The link leads inside the root, through a folder the app may not look into now: a place the walk couldn't
        // list, as that folder is itself, so what was found under the link before is kept — not a link it refuses.
        _folder.WriteFile("1", "media", "locked", "films", "x.mkv");
        var link = _folder.PathOf("media", "a-link");
        FileLinks.CreateFolderLink(link, "locked/films");
        var locked = _folder.PathOf("media", "locked");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        // Given its mode back in Dispose, which runs whatever happens here: a folder with mode 000 can't be emptied.
        _locked.Add(locked);
        if (CanList(locked))
            Assert.Skip("This process lists a folder with mode 000 anyway (it runs as root), so it can't see the refusal.");

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal(
            [
                Problem(FileListingStatus.Unlisted, link, $"The app isn't allowed to read “{link}”."),
                Problem(FileListingStatus.Unlisted, locked, $"The app isn't allowed to read “{locked}”."),
            ],
            entries);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux), Timeout = 10_000)]
    public async Task List_gives_a_named_pipe_named_like_media_without_opening_it()
    {
        //Arrange
        // Opened for reading, a pipe with no writer blocks until one comes: the walk would hang. It is given by its
        // name, and the inspection refuses it later without opening it either.
        CreateNamedPipe(_folder.PathOf("media", "movie.mkv"));

        //Act
        var entries = await ListAsync(_media);

        //Assert
        Assert.Equal([Media(_media, "movie.mkv")], entries);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<List<Listed>> ListAsync(params string[] roots)
    {
        var entries = new List<Listed>();
        await foreach (var entry in _inventory.ListMediaFilesAsync(roots, TestContext.Current.CancellationToken))
            entries.Add(InEnglish(entry));
        return entries;
    }

    /// <summary>A media file name reached by its own path.</summary>
    private static Listed Media(string folder, params string[] names)
    {
        var path = Canonical(Path.Combine([folder, .. names]));
        return (FileListingStatus.MediaFile, path, path, null);
    }

    private static Listed Problem(FileListingStatus status, string appPath, string error) =>
        (status, Canonical(appPath), null, error);

    private static string Canonical(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed.ToString() : throw new ArgumentException($"“{path}” is not a full path.", nameof(path));

    private static bool CanList(string folder)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(folder).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CreateNamedPipe(string path)
    {
        if (MakeFifo(path, Convert.ToUInt32("644", 8)) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    /// <summary>MAM-47 — an entry with its message in English: a refusal of the walk compares by the words a reader
    /// sees.</summary>
    private static Listed InEnglish(FileListingEntry entry) => (entry.Status, entry.AppPath, entry.ResolvedPath, TestTexts.En(entry.Error));
}
