using System.Runtime.Versioning;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-26 — the quarantine's renames and deletions over real folders (ADR 0030): a move and a restore keep the file's
// device and inode and leave nothing at the old name; a file that changed since it was checked, a name already taken
// in the quarantine, another mount, a quarantine folder that is a link, a folder or the file swapped right before the
// rename, a folder the app may not write to, and a library folder that leads into another allowed root or into a
// quarantine each move nothing; a restore never writes over a name, never makes a folder, never goes across mounts or
// into a quarantine, and refuses a folder swapped after its checks; a file swapped after the last look of all is
// renamed, and the answer says so by its own status; a deletion happens only inside a quarantine, reached with no
// link followed, to the file the caller expected. The expected facts are read by the real inventory, as the
// replacement's checks read them. Linux only: this is where the app runs, where renameat2 exists, and where CI runs the
// tests.
public sealed class LocalFileQuarantineLinuxTests : IDisposable
{
    private const string LinuxOnly = "The quarantine renames on Linux only; CI runs these tests there.";

    private static readonly Guid JobId = Guid.Parse("6f9d2c1e-4b7a-4e10-9c55-0d3a8e1f2b64");

    private readonly TemporaryFolder _folder = new("mam-quarantine-linux-");

    // The look never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());
    private readonly LocalFileQuarantine _quarantine = new();
    private readonly List<string> _locked = [];
    private readonly List<string> _elsewhere = [];
    private readonly string _media;

    public LocalFileQuarantineLinuxTests() => _media = _folder.CreateFolder("media");

    /// <summary>For <c>SkipUnless</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose()
    {
        // A folder with mode 0555 can't be emptied: it gets its mode back first.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var folder in _locked)
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        foreach (var folder in _elsewhere)
            Directory.Delete(folder, recursive: true);

        _folder.Dispose();
    }

    // ── moving ───────────────────────────────────────────────────────────────────────────────────

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_renames_the_file_into_its_roots_quarantine_keeping_its_device_and_inode_and_leaves_nothing_behind()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien (1979)", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var quarantinePath = QuarantinePathOf(film);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, quarantinePath, [_media], cancellationToken);

        //Assert
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, expected, null), moved);
        Assert.Equal(_folder.PathOf("media", ".mam-quarantine", JobId.ToString(), "movies", "Alien (1979)", "Alien.mkv"), quarantinePath);
        Assert.False(File.Exists(film));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
        Assert.Equal(expected, await FactsOfAsync(quarantinePath));
        // The folder stays, empty or not: only names move.
        Assert.True(Directory.Exists(_folder.PathOf("media", "movies", "Alien (1979)")));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_of_a_second_name_reuses_the_quarantine_folders_the_first_made()
    {
        //Arrange
        // A companion moves after its video, into the same folders: the app's own folders are there already.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien (1979)", "Alien.mkv");
        var subtitles = _folder.WriteFile("1\n00:00:01,000 --> 00:00:02,000\nIn space", "media", "movies", "Alien (1979)", "Alien.srt");
        var first = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), QuarantinePathOf(film), [_media], cancellationToken);
        var expected = await FactsOfAsync(subtitles);

        //Act
        var second = await _quarantine.MoveIntoQuarantineAsync(subtitles, expected, QuarantinePathOf(subtitles), [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Done, first.Status);
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, expected, null), second);
        Assert.Equal(["Alien.mkv", "Alien.srt"], Directory.GetFiles(Path.GetDirectoryName(QuarantinePathOf(film))!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Theory(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [InlineData("size")]
    [InlineData("modified")]
    [InlineData("identity")]
    public async Task Move_of_a_file_that_changed_since_it_was_checked_moves_nothing_and_makes_no_folder(string change)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        switch (change)
        {
            case "size":
                await File.AppendAllTextAsync(film, " and more", cancellationToken);
                File.SetLastWriteTimeUtc(film, expected.ModifiedUtc);
                break;
            case "modified":
                // One tick (100 ns) later: compared to the tick, as the look stores it.
                File.SetLastWriteTimeUtc(film, expected.ModifiedUtc.AddTicks(1));
                break;
            default:
                // Another file of the same size and time put at the name: only the inode tells it.
                var other = _folder.WriteFile("the new copy", "media", "other.mkv");
                File.SetLastWriteTimeUtc(other, expected.ModifiedUtc);
                File.Move(other, film, overwrite: true);
                break;
        }

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Changed,
                $"“{film}” changed since the app checked it: its size, modification time, device or inode isn't what was read then."),
            InEnglish(moved));
        Assert.True(File.Exists(film));
        Assert.False(Directory.Exists(_folder.PathOf("media", QuarantineFolder.Name)));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_onto_a_name_already_taken_in_the_quarantine_is_name_taken_and_both_files_stay()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var quarantinePath = QuarantinePathOf(film);
        Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)!);
        await File.WriteAllTextAsync(quarantinePath, "already here", cancellationToken);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, quarantinePath, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.NameTaken, $"“{quarantinePath}” already holds a file, and the app never writes over one."),
            InEnglish(moved));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(film, cancellationToken));
        Assert.Equal("already here", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_from_a_mount_inside_its_root_is_other_mount_and_makes_nothing()
    {
        //Arrange
        // /dev/shm, a tmpfs of its own, lies inside the allowed root /dev, where no folder needs a privilege to be there:
        // a rename from it into /dev/.mam-quarantine would be a copy. The mounts are compared before anything is made, so
        // not even .mam-quarantine is created — nothing is written to /dev.
        var cancellationToken = TestContext.Current.CancellationToken;
        var elsewhere = FolderOnSharedMemory();
        if (new DirectoryInfo("/dev/shm").LinkTarget is not null || MountIdOf(elsewhere) == MountIdOf("/dev"))
            Assert.Skip("/dev/shm isn't a mount of its own inside /dev here, so no rename across mounts can be shown.");
        var film = Path.Combine(elsewhere, "Alien.mkv");
        await File.WriteAllTextAsync(film, "the old copy", cancellationToken);
        string[] roots = ["/dev"];
        var expected = (await _inventory.DescribeAsync(film, roots, cancellationToken)).Value!;
        var quarantinePath = QuarantineFolder.PathOf(Parse("/dev"), JobId, Parse(film)).ToString();

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, quarantinePath, roots, cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.OtherMount, $"“{elsewhere}” and “/dev” are on different mounts, where a rename would be a copy."),
            InEnglish(moved));
        Assert.True(File.Exists(film));
        Assert.False(Directory.Exists(Path.Combine("/dev", QuarantineFolder.Name)));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_of_a_file_a_folder_link_leads_to_in_another_allowed_root_moves_nothing_and_makes_nothing()
    {
        //Arrange
        // /media/movies/Alien (1979) leads to /downloads/Alien.1979, another allowed root on the same mount: a rename into
        // /media/.mam-quarantine would work, but a restore, which reaches back through /media alone, could never put it
        // back. The move stays inside the root whose quarantine it uses, so nothing moves and nothing is made.
        var cancellationToken = TestContext.Current.CancellationToken;
        var downloads = _folder.CreateFolder("downloads");
        var target = _folder.WriteFile("the old copy", "downloads", "Alien.1979", "Alien.mkv");
        var link = _folder.PathOf("media", "movies", "Alien (1979)");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        FileLinks.CreateFolderLink(link, _folder.PathOf("downloads", "Alien.1979"));
        var film = Path.Combine(link, "Alien.mkv");
        string[] roots = [_media, downloads];
        var expected = (await _inventory.DescribeAsync(film, roots, cancellationToken)).Value!;

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), roots, cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.OutsideAllowedRoots,
                $"“{link}” leads through a link to “{_folder.PathOf("downloads", "Alien.1979")}”, outside the media folders the app may read."),
            InEnglish(moved));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(target, cancellationToken));
        Assert.False(Directory.Exists(_folder.PathOf("media", QuarantineFolder.Name)));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_through_a_folder_link_into_another_jobs_quarantine_moves_nothing()
    {
        //Arrange
        // /media/movies was made a link into another replacement's folder in the quarantine: what is there is that
        // replacement's, and the app never moves a name out of one quarantine folder into another.
        var cancellationToken = TestContext.Current.CancellationToken;
        var otherJob = _folder.PathOf("media", QuarantineFolder.Name, Guid.NewGuid().ToString(), "movies");
        var quarantined = _folder.WriteFile("another replacement's old copy", [.. RelativeTo(otherJob), "Alien.mkv"]);
        FileLinks.CreateFolderLink(_folder.PathOf("media", "movies"), otherJob);
        var film = _folder.PathOf("media", "movies", "Alien.mkv");
        var expected = await FactsOfAsync(film);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, $"“{film}” already lies in a quarantine."),
            InEnglish(moved));
        Assert.Equal("another replacement's old copy", await File.ReadAllTextAsync(quarantined, cancellationToken));
        Assert.False(Directory.Exists(_folder.PathOf("media", QuarantineFolder.Name, JobId.ToString())));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_where_the_quarantine_folder_is_a_link_moves_nothing_through_it()
    {
        //Arrange
        // Someone who may write in /media planted .mam-quarantine as a link to a folder outside: the app makes folders
        // in its own quarantine only, reached with no link followed.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var outside = _folder.CreateFolder("outside");
        var planted = _folder.PathOf("media", QuarantineFolder.Name);
        FileLinks.CreateFolderLink(planted, outside);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Changed, moved.Status);
        Assert.Equal(
            $"“{planted}” changed while the app was checking it: a folder on the way or the file itself became a link or something else.",
            TestTexts.En(moved.Error));
        Assert.True(File.Exists(film));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_where_the_files_folder_is_swapped_for_a_link_right_before_the_rename_moves_nothing()
    {
        //Arrange
        // After every check, the film's folder moves aside and a link to a folder outside takes its place. The rename
        // would go through the folder held, so nothing outside could be touched either way; it is refused, so the path
        // the job records is where the film really was.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien (1979)", "Alien.mkv");
        var outsideFilm = _folder.WriteFile("someone else's file", "outside", "Alien.mkv");
        var folder = _folder.PathOf("media", "movies", "Alien (1979)");
        var aside = _folder.PathOf("media", "movies", "aside");
        var expected = await FactsOfAsync(film);
        var quarantine = new LocalFileQuarantine
        {
            BeforeWrite = _ =>
            {
                Directory.Move(folder, aside);
                FileLinks.CreateFolderLink(folder, _folder.PathOf("outside"));
            },
        };

        //Act
        var moved = await quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Changed,
                $"“{film}” changed while the app was checking it: a folder on the way or the file itself became a link or something else."),
            InEnglish(moved));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(Path.Combine(aside, "Alien.mkv"), cancellationToken));
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(outsideFilm, cancellationToken));
        Assert.False(File.Exists(QuarantinePathOf(film)));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_where_the_file_is_swapped_for_another_right_before_the_rename_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var aside = _folder.PathOf("media", "movies", "aside.mkv");
        var expected = await FactsOfAsync(film);
        var quarantine = new LocalFileQuarantine
        {
            BeforeWrite = _ =>
            {
                File.Move(film, aside);
                File.WriteAllText(film, "another file put at its name");
            },
        };

        //Act
        var moved = await quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Changed, moved.Status);
        Assert.Equal("another file put at its name", await File.ReadAllTextAsync(film, cancellationToken));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(aside, cancellationToken));
        Assert.False(File.Exists(QuarantinePathOf(film)));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_where_another_file_takes_the_name_after_the_last_look_is_swapped_and_says_what_moved()
    {
        //Arrange
        // The one moment no look can see: after the last one, right before the rename. What the rename moved is the other
        // file, which the answer says by a status of its own, so the caller never records it as the old copy.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var aside = _folder.PathOf("media", "movies", "aside.mkv");
        var expected = await FactsOfAsync(film);
        var quarantinePath = QuarantinePathOf(film);
        var quarantine = new LocalFileQuarantine
        {
            BeforeRenameCall = _ =>
            {
                File.Move(film, aside);
                File.WriteAllText(film, "another file put at its name");
            },
        };

        //Act
        var moved = await quarantine.MoveIntoQuarantineAsync(film, expected, quarantinePath, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Swapped,
                $"What the app renamed from “{film}” to “{quarantinePath}” isn't the file it checked: another file was put at that name just before the rename."),
            InEnglish(moved));
        Assert.Equal("another file put at its name", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(aside, cancellationToken));
        Assert.False(File.Exists(film));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Move_of_a_link_moves_nothing()
    {
        //Arrange
        // The look follows the link, so the checks saw the file it leads to; the name itself is a link, which never
        // moves — renaming it would leave the file where it is.
        var cancellationToken = TestContext.Current.CancellationToken;
        var target = _folder.WriteFile("the old copy", "media", "store", "Alien.mkv");
        var film = _folder.PathOf("media", "movies", "Alien.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(film)!);
        Assert.True(FileLinks.TryCreateSymbolicLink(film, target));
        var expected = await FactsOfAsync(film);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(Refusal<FileFacts>(FileQuarantineStatus.Changed, $"“{film}” isn't a regular file."), InEnglish(moved));
        Assert.True(File.Exists(target));
        Assert.NotNull(new FileInfo(film).LinkTarget);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [UnsupportedOSPlatform("windows")]
    public async Task Move_out_of_a_folder_the_app_may_not_write_to_is_not_allowed_and_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var folder = _folder.PathOf("media", "movies");
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        _locked.Add(folder);
        if (CanWrite(folder))
            Assert.Skip("This process writes into a folder with mode 0555 anyway (it runs as root), so it can't see the refusal.");

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, QuarantinePathOf(film), [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.NotAllowed,
                $"The app's user isn't allowed to change “{film}”: renaming or deleting a file needs write and search permission on its folder, and on the one it goes into."),
            InEnglish(moved));
        Assert.True(File.Exists(film));
        Assert.False(File.Exists(QuarantinePathOf(film)));
    }

    [Theory(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    // Not in a quarantine at all.
    [InlineData("movies/Alien.mkv")]
    // A quarantine below the top of the root, which the app never makes.
    [InlineData("movies/.mam-quarantine/6f9d2c1e-4b7a-4e10-9c55-0d3a8e1f2b64/Alien.mkv")]
    // The quarantine itself, with no job's folder.
    [InlineData(".mam-quarantine/Alien.mkv")]
    public async Task Move_to_a_place_that_isnt_a_jobs_folder_in_the_roots_quarantine_moves_nothing(string destination)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var quarantinePath = _folder.PathOf(["media", .. destination.Split('/')]);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, quarantinePath, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.OutsideAllowedRoots, $"“{quarantinePath}” isn't inside the quarantine at the top of a media folder."),
            InEnglish(moved));
        Assert.True(File.Exists(film));
    }

    // ── restoring ────────────────────────────────────────────────────────────────────────────────

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_renames_the_file_back_keeping_its_device_and_inode_and_leaves_nothing_in_the_quarantine()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien (1979)", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, expected, quarantinePath, [_media], cancellationToken);

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Done, moved.Status);
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, expected, null), restored);
        Assert.Equal(expected, await FactsOfAsync(film));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(film, cancellationToken));
        Assert.False(File.Exists(quarantinePath));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_onto_a_path_that_holds_a_file_again_is_name_taken_and_both_files_stay()
    {
        //Arrange
        // A new copy landed where the old one was: the app never writes over it.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        await File.WriteAllTextAsync(film, "the new copy", cancellationToken);

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(FileQuarantineStatus.NameTaken, $"“{film}” already holds a file, and the app never writes over one."),
            InEnglish(restored));
        Assert.Equal("the new copy", await File.ReadAllTextAsync(film, cancellationToken));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_into_a_folder_that_is_gone_is_folder_gone_and_makes_no_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien (1979)", "Alien.mkv");
        var folder = _folder.PathOf("media", "movies", "Alien (1979)");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        Directory.Delete(folder);

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.FolderGone, $"The folder “{folder}” is gone, and the app creates no folder to put a file back."),
            InEnglish(restored));
        Assert.False(Directory.Exists(folder));
        Assert.True(File.Exists(quarantinePath));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_of_a_quarantined_file_that_changed_puts_nothing_back()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        await File.AppendAllTextAsync(quarantinePath, " and more", cancellationToken);

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Changed, restored.Status);
        Assert.False(File.Exists(film));
        Assert.True(File.Exists(quarantinePath));
    }

    [Theory(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [InlineData("swapped for a link")]
    [InlineData("removed")]
    public async Task Restore_where_the_folder_it_goes_back_into_changes_after_the_checks_puts_nothing_back(string change)
    {
        //Arrange
        // After every check, the folder the film goes back into is swapped for a link to a folder outside, or removed: the
        // rename would go through the folder held, and is refused so the path the job records is where it really went.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien (1979)", "Alien.mkv");
        var folder = _folder.PathOf("media", "movies", "Alien (1979)");
        var aside = _folder.PathOf("media", "movies", "aside");
        var outside = _folder.CreateFolder("outside");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        var quarantine = new LocalFileQuarantine
        {
            BeforeWrite = _ =>
            {
                Directory.Move(folder, aside);
                if (change == "swapped for a link")
                    FileLinks.CreateFolderLink(folder, outside);
            },
        };

        //Act
        var restored = await quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            change == "removed"
                ? Refusal<FileFacts>(
                    FileQuarantineStatus.FolderGone, $"The folder “{folder}” is gone, and the app creates no folder to put a file back.")
                : Refusal<FileFacts>(
                    FileQuarantineStatus.Changed,
                    $"“{film}” changed while the app was checking it: a folder on the way or the file itself became a link or something else."),
            InEnglish(restored));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.Empty(Directory.EnumerateFileSystemEntries(aside));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_where_another_file_takes_the_name_in_the_quarantine_after_the_last_look_is_swapped()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var aside = Path.Combine(Path.GetDirectoryName(quarantinePath)!, "aside.mkv");
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        var quarantine = new LocalFileQuarantine
        {
            BeforeRenameCall = _ =>
            {
                File.Move(quarantinePath, aside);
                File.WriteAllText(quarantinePath, "another file put at its name");
            },
        };

        //Act
        var restored = await quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Swapped,
                $"What the app renamed from “{quarantinePath}” to “{film}” isn't the file it checked: another file was put at that name just before the rename."),
            InEnglish(restored));
        Assert.Equal("another file put at its name", await File.ReadAllTextAsync(film, cancellationToken));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(aside, cancellationToken));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_into_a_folder_on_another_mount_than_the_quarantine_is_other_mount_and_puts_nothing_back()
    {
        //Arrange
        // /media/fast now leads to a second allowed root on /dev/shm, a tmpfs of its own: the rename back would be a copy.
        // The file was put in the quarantine by hand, as no move could have taken it there across the mounts.
        var cancellationToken = TestContext.Current.CancellationToken;
        var elsewhere = FolderOnSharedMemory();
        if (MountIdOf(elsewhere) == MountIdOf(_media))
            Assert.Skip("/dev/shm is on the temp folder's mount here, so no rename across mounts can be shown.");
        var link = _folder.PathOf("media", "fast");
        FileLinks.CreateFolderLink(link, elsewhere);
        var film = Path.Combine(link, "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        _folder.WriteFile("the old copy", RelativeTo(quarantinePath));
        var expected = await FactsOfAsync(quarantinePath);

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantinePath, expected, film, [_media, elsewhere], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.OtherMount,
                $"“{Path.GetDirectoryName(quarantinePath)}” and “{link}” are on different mounts, where a rename would be a copy."),
            InEnglish(restored));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Restore_into_a_folder_that_leads_into_another_jobs_quarantine_puts_nothing_back()
    {
        //Arrange
        // /media/movies was made a link into another replacement's folder in the quarantine: a name put back there would
        // sit where no scan looks, recorded as back in the library.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        var otherJob = _folder.CreateFolder("media", QuarantineFolder.Name, Guid.NewGuid().ToString(), "movies");
        Directory.Delete(_folder.PathOf("media", "movies"));
        FileLinks.CreateFolderLink(_folder.PathOf("media", "movies"), otherJob);

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantinePath, moved.Value!, film, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(FileQuarantineStatus.OutsideAllowedRoots, $"“{film}” already lies in a quarantine."),
            InEnglish(restored));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(otherJob));
    }

    // ── deleting ─────────────────────────────────────────────────────────────────────────────────

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Delete_of_an_unchanged_quarantined_file_deletes_that_name_and_no_folder()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);

        //Act
        var deleted = await _quarantine.DeleteFromQuarantineAsync(quarantinePath, moved.Value!, [_media], cancellationToken);

        //Assert
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, moved.Value, null), deleted);
        Assert.False(File.Exists(quarantinePath));
        Assert.True(Directory.Exists(Path.GetDirectoryName(quarantinePath)));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Delete_of_a_quarantined_file_that_changed_deletes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        await File.AppendAllTextAsync(quarantinePath, " and more", cancellationToken);

        //Act
        var deleted = await _quarantine.DeleteFromQuarantineAsync(quarantinePath, moved.Value!, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Changed,
                $"“{quarantinePath}” changed since the app checked it: its size, modification time, device or inode isn't what was read then."),
            InEnglish(deleted));
        Assert.Equal("the old copy and more", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Delete_where_a_folder_inside_the_jobs_quarantine_is_a_link_to_the_library_deletes_nothing()
    {
        //Arrange
        // Someone who may write in /media made the job's own "movies" folder in the quarantine a link to the library's:
        // the quarantine is reached with no link followed, so the library's film is never reached through it.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the library's copy", "media", "movies", "Alien.mkv");
        var expected = await FactsOfAsync(film);
        var planted = _folder.PathOf("media", QuarantineFolder.Name, JobId.ToString(), "movies");
        Directory.CreateDirectory(Path.GetDirectoryName(planted)!);
        FileLinks.CreateFolderLink(planted, _folder.PathOf("media", "movies"));

        //Act
        var deleted = await _quarantine.DeleteFromQuarantineAsync(QuarantinePathOf(film), expected, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Changed,
                $"“{planted}” changed while the app was checking it: a folder on the way or the file itself became a link or something else."),
            InEnglish(deleted));
        Assert.Equal("the library's copy", await File.ReadAllTextAsync(film, cancellationToken));
    }

    [Theory(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    [InlineData("the file")]
    [InlineData("the folder")]
    public async Task Delete_where_the_file_or_its_folder_is_swapped_right_before_the_deletion_deletes_nothing(string swapped)
    {
        //Arrange
        // After every check: another file put at the quarantined name, or the job's folder moved aside and a link to a
        // folder outside, holding a file of that name, put in its place.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        var quarantinePath = QuarantinePathOf(film);
        var leaf = Path.GetDirectoryName(quarantinePath)!;
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, await FactsOfAsync(film), quarantinePath, [_media], cancellationToken);
        var outsideFilm = _folder.WriteFile("someone else's file", "outside", "Alien.mkv");
        var aside = swapped == "the file" ? Path.Combine(leaf, "aside.mkv") : _folder.PathOf("media", QuarantineFolder.Name, "aside");
        var quarantine = new LocalFileQuarantine
        {
            BeforeWrite = _ =>
            {
                if (swapped == "the file")
                {
                    File.Move(quarantinePath, aside);
                    File.WriteAllText(quarantinePath, "another file put at its name");
                }
                else
                {
                    Directory.Move(leaf, aside);
                    FileLinks.CreateFolderLink(leaf, _folder.PathOf("outside"));
                }
            },
        };

        //Act
        var deleted = await quarantine.DeleteFromQuarantineAsync(quarantinePath, moved.Value!, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.Changed,
                swapped == "the file"
                    ? $"“{quarantinePath}” changed since the app checked it: its size, modification time, device or inode isn't what was read then."
                    : $"“{quarantinePath}” changed while the app was checking it: a folder on the way or the file itself became a link or something else."),
            InEnglish(deleted));
        Assert.Equal("the old copy", await File.ReadAllTextAsync(swapped == "the file" ? aside : Path.Combine(aside, "Alien.mkv"), cancellationToken));
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(outsideFilm, cancellationToken));
        if (swapped == "the file")
            Assert.Equal("another file put at its name", await File.ReadAllTextAsync(quarantinePath, cancellationToken));
    }

    [Theory(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    // A library file.
    [InlineData("movies/Alien.mkv")]
    // A quarantine below the top of the root, which the app never makes.
    [InlineData("movies/.mam-quarantine/6f9d2c1e-4b7a-4e10-9c55-0d3a8e1f2b64/Alien.mkv")]
    // The quarantine itself, with no job's folder.
    [InlineData(".mam-quarantine/Alien.mkv")]
    public async Task Delete_of_a_file_outside_a_jobs_folder_in_the_roots_quarantine_deletes_nothing(string place)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = _folder.WriteFile("a file", ["media", .. place.Split('/')]);
        var facts = await FactsOfAsync(path);

        //Act
        var deleted = await _quarantine.DeleteFromQuarantineAsync(path, facts, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<FileFacts>(
                FileQuarantineStatus.OutsideAllowedRoots, $"“{path}” isn't inside the quarantine at the top of a media folder."),
            InEnglish(deleted));
        Assert.True(File.Exists(path));
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task Delete_in_the_quarantine_of_a_folder_that_isnt_allowed_deletes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var other = _folder.CreateFolder("other");
        var path = _folder.WriteFile("a file", "other", QuarantineFolder.Name, JobId.ToString(), "Alien.mkv");
        var facts = (await _inventory.DescribeAsync(path, [other], cancellationToken)).Value!;

        //Act
        var deleted = await _quarantine.DeleteFromQuarantineAsync(path, facts, [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.OutsideAllowedRoots, deleted.Status);
        Assert.True(File.Exists(path));
    }

    // ── listing ──────────────────────────────────────────────────────────────────────────────────

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux), Timeout = 10_000)]
    public async Task ListFolderFiles_leaves_out_a_named_pipe_and_a_link_and_never_opens_either()
    {
        //Arrange
        // Opened for reading, a pipe with no writer blocks until one comes: the listing reads each name with statx only.
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "movies", "Alien.mkv");
        LinuxProbes.CreateNamedPipe(_folder.PathOf("media", "movies", "Alien.srt"));
        Assert.True(FileLinks.TryCreateSymbolicLink(_folder.PathOf("media", "movies", "Alien.nfo"), film));

        //Act
        var listed = await _quarantine.ListFolderFilesAsync(film, [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Done, listed.Status);
        Assert.Equal([new FolderFile("Alien.mkv", film, await FactsOfAsync(film))], listed.Value);
    }

    [Fact(Skip = LinuxOnly, SkipUnless = nameof(IsLinux))]
    public async Task ListFolderFiles_of_a_folder_that_leads_into_a_quarantine_lists_nothing()
    {
        //Arrange
        // /media/movies was made a link into a replacement's folder in the quarantine: what is there is never a companion.
        var cancellationToken = TestContext.Current.CancellationToken;
        var otherJob = _folder.PathOf("media", QuarantineFolder.Name, Guid.NewGuid().ToString(), "movies");
        _folder.WriteFile("another replacement's old copy", [.. RelativeTo(otherJob), "Alien.mkv"]);
        FileLinks.CreateFolderLink(_folder.PathOf("media", "movies"), otherJob);
        var film = _folder.PathOf("media", "movies", "Alien.mkv");

        //Act
        var listed = await _quarantine.ListFolderFilesAsync(film, [_media], cancellationToken);

        //Assert
        Assert.Equal(
            Refusal<IReadOnlyList<FolderFile>>(FileQuarantineStatus.OutsideAllowedRoots, $"“{film}” already lies in a quarantine."),
            InEnglish(listed));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<FileFacts> FactsOfAsync(string path) =>
        (await _inventory.DescribeAsync(path, [_media], TestContext.Current.CancellationToken)).Value
        ?? throw new InvalidOperationException($"“{path}” couldn't be described.");

    /// <summary>Where the replacement job puts the copy at <paramref name="appPath"/>: its root's quarantine.</summary>
    private string QuarantinePathOf(string appPath) =>
        QuarantineFolder.PathOf(Parse(_media), JobId, Parse(appPath)).ToString();

    /// <summary>A folder of its own on <c>/dev/shm</c>, a tmpfs mounted apart from the temp folder; removed in
    /// <see cref="Dispose"/>.</summary>
    private string FolderOnSharedMemory()
    {
        if (!Directory.Exists("/dev/shm"))
            Assert.Skip("This system has no /dev/shm, so no second mount is at hand.");

        var folder = Directory.CreateDirectory(Path.Combine("/dev/shm", $"mam-quarantine-{Guid.NewGuid():N}")).FullName;
        _elsewhere.Add(folder);
        return folder;
    }

    /// <summary>The names of <paramref name="path"/> below the test's own folder, for <see cref="TemporaryFolder"/>'s
    /// calls.</summary>
    private string[] RelativeTo(string path) => Path.GetRelativePath(_folder.FullName, path).Split(Path.DirectorySeparatorChar);

    private static ulong MountIdOf(string path) =>
        LinuxProbes.MountIdOf(path) ?? throw new InvalidOperationException("This kernel doesn't report mount ids (Linux 5.8 or later does).");

    private static NormalizedPath Parse(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed : throw new ArgumentException($"“{path}” is not a full path.", nameof(path));

    private static bool CanWrite(string folder)
    {
        var probe = Path.Combine(folder, ".write-probe");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>MAM-47 — a result with its message in English: a refusal compares by the words a reader sees.</summary>
    private static (FileQuarantineStatus Status, T? Value, string? Error) InEnglish<T>(FileQuarantineResult<T> result) where T : class =>
        (result.Status, result.Value, TestTexts.En(result.Error));

    /// <summary>MAM-47 — a refusal as <see cref="InEnglish{T}"/> gives it.</summary>
    private static (FileQuarantineStatus Status, T? Value, string? Error) Refusal<T>(FileQuarantineStatus status, string error) where T : class =>
        (status, null, error);
}
