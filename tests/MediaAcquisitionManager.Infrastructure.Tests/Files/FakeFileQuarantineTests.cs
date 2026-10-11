using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-26 — the fake write contract every HTTP test of the quarantine stands on, kept to the promises of IFileQuarantine
// (ADR 0030): a rename moves the planted file to its quarantine name with its identity, so the fake file inventory finds
// it there and nothing at the old name; the only folders it makes are the quarantine's; a changed file, a name taken,
// another mount, a read-only mount, a folder the app may not write in, a path outside the roots or out of a quarantine,
// and a platform that can't rename each move nothing; a restore never makes the folder it needs; a deletion touches only
// a quarantined name. The listing beside a video, the hint's answers, the hooks and the walk's skip of the quarantine are
// pinned too, so a test of the app can't pass on a fake that drifted from the contract.
public sealed class FakeFileQuarantineTests
{
    private const string Root = "/media";
    private const string Folder = "/media/movies/Alien (1979)";
    private const string VideoPath = "/media/movies/Alien (1979)/Alien (1979).mkv";
    private const string SubtitlePath = "/media/movies/Alien (1979)/Alien (1979).uk.srt";
    private const string QuarantinedVideoPath = "/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies/Alien (1979)/Alien (1979).mkv";

    private static readonly string[] Roots = [Root];

    private readonly FakeFileInventory _files = new();
    private readonly FakeFileQuarantine _quarantine;

    public FakeFileQuarantineTests() => _quarantine = new FakeFileQuarantine(_files);

    [Fact]
    public async Task Move_renames_the_file_into_the_quarantine_with_its_identity_making_only_the_quarantines_folders()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var facts = TestMedia.File(inode: 1);
        _files.AddFile(VideoPath, facts, TestMedia.Taken());

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, facts, null), moved);
        Assert.Equal(FileInventoryStatus.NotFound, (await _files.DescribeAsync(VideoPath, Roots, cancellationToken)).Status);
        Assert.Equal(facts, (await _files.DescribeAsync(QuarantinedVideoPath, Roots, cancellationToken)).Value);
        Assert.Equal([(VideoPath, QuarantinedVideoPath)], _quarantine.Renames);
        Assert.Equal(
            [
                "/media/.mam-quarantine",
                "/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77",
                "/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies",
                "/media/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/movies/Alien (1979)",
            ],
            _quarantine.CreatedFolders);
        // The video's folder stays, empty, as on a disk.
        Assert.True(_files.IsFolder(Folder));
    }

    [Fact]
    public async Task Move_of_a_file_that_changed_since_the_check_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1, sizeBytes: 1_000));

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, TestMedia.File(inode: 1), QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(
            (FileQuarantineStatus.Changed, $"“{VideoPath}” changed since the app checked it."), (moved.Status, moved.Error));
        await AssertNothingMovedAsync(cancellationToken);
    }

    [Fact]
    public async Task Move_onto_a_name_already_in_the_quarantine_replaces_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        _files.AddFile(QuarantinedVideoPath, TestMedia.File(inode: 99));

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, TestMedia.File(inode: 1), QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(
            (FileQuarantineStatus.NameTaken, $"Something is already at “{QuarantinedVideoPath}”, and the app never renames over it."),
            (moved.Status, moved.Error));
        Assert.Equal((long?)99, (await _files.DescribeAsync(QuarantinedVideoPath, Roots, cancellationToken)).Value!.Inode);
        await AssertNothingMovedAsync(cancellationToken);
    }

    [Fact]
    public async Task Move_from_another_mount_than_the_quarantines_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        _quarantine.SetMount("/media/movies", 2);

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, TestMedia.File(inode: 1), QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.OtherMount, moved.Status);
        await AssertNothingMovedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("not writable")]
    [InlineData("cannot rename")]
    public async Task Move_where_the_host_refuses_a_write_moves_nothing(string refusal)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        var expected = refusal switch
        {
            "read-only" => FileQuarantineStatus.ReadOnly,
            "not writable" => FileQuarantineStatus.NotAllowed,
            _ => FileQuarantineStatus.NotSupported,
        };
        switch (refusal)
        {
            case "read-only":
                _quarantine.SetReadOnly(Root);
                break;
            case "not writable":
                _quarantine.SetNotWritable(Folder);
                break;
            default:
                _quarantine.Capabilities = new FileQuarantineCapabilities(CanRename: false);
                break;
        }

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, TestMedia.File(inode: 1), QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(expected, moved.Status);
        await AssertNothingMovedAsync(cancellationToken);
    }

    [Theory]
    // No roots, a file outside them, and a place outside the root's quarantine (its folder itself included).
    [InlineData("", QuarantinedVideoPath)]
    [InlineData("/downloads", QuarantinedVideoPath)]
    [InlineData(Root, "/media/movies/Alien (1979) old/Alien (1979).mkv")]
    [InlineData(Root, "/media/.mam-quarantine")]
    public async Task Move_outside_the_roots_or_out_of_the_roots_quarantine_moves_nothing(string root, string quarantinePath)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        string[] roots = root.Length == 0 ? [] : [root];

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, TestMedia.File(inode: 1), quarantinePath, roots, cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.OutsideAllowedRoots, moved.Status);
        await AssertNothingMovedAsync(cancellationToken);
    }

    [Fact]
    public async Task Refuse_answers_its_status_for_the_path_and_moves_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        _quarantine.Refuse(VideoPath, FileQuarantineStatus.Failed, "planted-refusal-0026");

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(VideoPath, TestMedia.File(inode: 1), QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal((FileQuarantineStatus.Failed, (string?)"planted-refusal-0026"), (moved.Status, moved.Error));
        await AssertNothingMovedAsync(cancellationToken);
    }

    [Theory]
    [InlineData("into the quarantine")]
    [InlineData("out of it")]
    public async Task Swap_moves_the_other_file_to_the_new_name_once_and_answers_swapped(string direction)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var facts = TestMedia.File(inode: 1);
        var other = TestMedia.File(inode: 99, sizeBytes: 5);
        _files.AddFile(VideoPath, facts);
        if (direction == "out of it")
            await _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken);
        var (from, to) = direction == "out of it" ? (QuarantinedVideoPath, VideoPath) : (VideoPath, QuarantinedVideoPath);
        _quarantine.Swap(from, other);

        //Act
        var swapped = direction == "out of it"
            ? await _quarantine.RestoreFromQuarantineAsync(from, facts, to, Roots, cancellationToken)
            : await _quarantine.MoveIntoQuarantineAsync(from, facts, to, Roots, cancellationToken);

        //Assert
        Assert.Equal(
            new FileQuarantineResult<FileFacts>(
                FileQuarantineStatus.Swapped,
                null,
                $"What the app renamed from “{from}” to “{to}” isn't the file it checked: another file was put at that name just before the rename."),
            swapped);
        Assert.Equal(other, (await _files.DescribeAsync(to, Roots, cancellationToken)).Value);
        Assert.Equal(FileInventoryStatus.NotFound, (await _files.DescribeAsync(from, Roots, cancellationToken)).Status);
        Assert.Equal((from, to), _quarantine.Renames[^1]);
    }

    [Fact]
    public async Task Restore_puts_the_file_back_but_never_into_a_folder_that_is_gone()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var facts = TestMedia.File(inode: 1);
        _files.AddFile(VideoPath, facts);
        await _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken);
        _files.RemoveFolder(Folder);

        //Act
        var refused = await _quarantine.RestoreFromQuarantineAsync(QuarantinedVideoPath, facts, VideoPath, Roots, cancellationToken);
        var foldersAfterRefusal = _quarantine.CreatedFolders.Count;
        _files.AddFolder(Folder);
        var restored = await _quarantine.RestoreFromQuarantineAsync(QuarantinedVideoPath, facts, VideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(
            (FileQuarantineStatus.FolderGone, $"The folder “{Folder}” is gone, and the app creates no folder for it."), (refused.Status, refused.Error));
        Assert.Equal(4, foldersAfterRefusal);
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, facts, null), restored);
        Assert.Equal(facts, (await _files.DescribeAsync(VideoPath, Roots, cancellationToken)).Value);
        Assert.Equal([(VideoPath, QuarantinedVideoPath), (QuarantinedVideoPath, VideoPath)], _quarantine.Renames);
    }

    [Fact]
    public async Task Restore_onto_a_new_file_at_the_old_path_replaces_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var facts = TestMedia.File(inode: 1);
        _files.AddFile(VideoPath, facts);
        await _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken);
        _files.AddFile(VideoPath, TestMedia.File(inode: 7));

        //Act
        var restored = await _quarantine.RestoreFromQuarantineAsync(QuarantinedVideoPath, facts, VideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.NameTaken, restored.Status);
        Assert.Equal((long?)7, (await _files.DescribeAsync(VideoPath, Roots, cancellationToken)).Value!.Inode);
        Assert.Equal(facts, (await _files.DescribeAsync(QuarantinedVideoPath, Roots, cancellationToken)).Value);
    }

    [Fact]
    public async Task Delete_removes_a_quarantined_name_and_nothing_outside_a_quarantine()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var facts = TestMedia.File(inode: 1);
        _files.AddFile(VideoPath, facts);
        _files.AddFile(SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 80_000));
        await _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken);

        //Act
        var outside = await _quarantine.DeleteFromQuarantineAsync(SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 80_000), Roots, cancellationToken);
        var deleted = await _quarantine.DeleteFromQuarantineAsync(QuarantinedVideoPath, facts, Roots, cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.OutsideAllowedRoots, outside.Status);
        Assert.Equal(new FileQuarantineResult<FileFacts>(FileQuarantineStatus.Done, facts, null), deleted);
        Assert.Equal([QuarantinedVideoPath], _quarantine.Deletions);
        Assert.Equal(FileInventoryStatus.NotFound, (await _files.DescribeAsync(QuarantinedVideoPath, Roots, cancellationToken)).Status);
        Assert.Equal(FileInventoryStatus.Found, (await _files.DescribeAsync(SubtitlePath, Roots, cancellationToken)).Status);
    }

    [Fact]
    public async Task ListFolderFiles_lists_the_regular_files_beside_the_video_with_their_facts()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        _files.AddFile(SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 80_000));
        // Below the folder, a linked name and a planted problem: none is a regular file of the folder.
        _files.AddFile($"{Folder}/Extras/Alien (1979).making-of.mkv", TestMedia.File(inode: 3));
        _files.AddLinkedName($"{Folder}/Alien (1979).link.mkv", VideoPath);
        _files.AddProblem($"{Folder}/Alien (1979).nfo", FileInventoryStatus.Unreadable, "planted-problem-0026");

        //Act
        var listed = await _quarantine.ListFolderFilesAsync(VideoPath, Roots, cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Done, listed.Status);
        Assert.Equal(
            [
                new FolderFile("Alien (1979).mkv", VideoPath, TestMedia.File(inode: 1)),
                new FolderFile("Alien (1979).uk.srt", SubtitlePath, TestMedia.File(inode: 2, sizeBytes: 80_000)),
            ],
            listed.Value!);
    }

    [Fact]
    public async Task ListFolderNames_gives_every_name_the_store_holds_in_the_folder_a_dangling_linked_name_too()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        _files.AddLinkedName(SubtitlePath, "/media/nowhere/Alien (1979).uk.srt");
        _files.AddFolder($"{Folder}/Extras");

        //Act
        var listed = await _quarantine.ListFolderNamesAsync(VideoPath, Roots, cancellationToken);
        var gone = await _quarantine.ListFolderNamesAsync("/media/movies/Gone (2000)/Gone.mkv", Roots, cancellationToken);
        var outside = await _quarantine.ListFolderNamesAsync("/elsewhere/Alien.mkv", Roots, cancellationToken);

        //Assert
        // The linked name leads nowhere — a look at it finds nothing —, and is listed all the same.
        Assert.Equal(FileInventoryStatus.NotFound, (await _files.DescribeAsync(SubtitlePath, Roots, cancellationToken)).Status);
        Assert.Equal(FileQuarantineStatus.Done, listed.Status);
        Assert.Equal(["Alien (1979).mkv", "Alien (1979).uk.srt", "Extras"], listed.Value);
        Assert.Equal((FileQuarantineStatus.NotFound, FileQuarantineStatus.OutsideAllowedRoots), (gone.Status, outside.Status));
    }

    [Fact]
    public async Task DescribeRenamePlaces_answers_from_the_planted_mounts_one_per_folder_in_order()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFolder("/media/movies");
        _files.AddFolder("/media/tv-shows");
        _files.AddFolder("/media/documentary");
        _quarantine.SetMount("/media/tv-shows", 2);
        _quarantine.SetReadOnly("/media/documentary");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync(
            ["/media/movies", "/media/tv-shows", "/media/documentary", "/media/cartoons"], Roots, cancellationToken);

        //Assert
        Assert.Equal(
            [
                ("/media/movies", ""),
                ("/media/tv-shows", "OtherMount"),
                ("/media/documentary", "ReadOnly"),
                ("/media/cartoons", "CouldNotCheck"),
            ],
            places.Select(place => (place.FolderAppPath, string.Join(",", place.Problems))));
    }

    [Fact]
    public async Task Hooks_run_before_the_checks_and_after_the_rename()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var facts = TestMedia.File(inode: 1);
        _files.AddFile(VideoPath, facts);
        FileInventoryStatus? seenAfter = null;
        _quarantine.AfterRename = async (_, to, token) => seenAfter = (await _files.DescribeAsync(to, Roots, token)).Status;
        _quarantine.BeforeRename = (_, _, _) => throw new InvalidOperationException("planted-exception-message-0026");

        //Act
        var stopped = await Record.ExceptionAsync(() => _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken));
        var renamesAfterStop = _quarantine.Renames.Count;
        _quarantine.BeforeRename = null;
        await _quarantine.MoveIntoQuarantineAsync(VideoPath, facts, QuarantinedVideoPath, Roots, cancellationToken);

        //Assert
        // An app stopping before the write moved nothing; after the rename the file is already at its new name.
        Assert.IsType<InvalidOperationException>(stopped);
        Assert.Equal(0, renamesAfterStop);
        Assert.Equal(FileInventoryStatus.Found, seenAfter);
    }

    [Fact]
    public async Task The_inventorys_walk_leaves_out_every_quarantine_and_every_link_into_one()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _files.AddFile(VideoPath, TestMedia.File(inode: 1));
        _files.AddFile(QuarantinedVideoPath, TestMedia.File(inode: 2));
        _files.AddFile("/media/movies/.mam-quarantine/0f8b2a54-7c1e-4d39-9a6b-3e5d2c1f0a77/Aliens.mkv", TestMedia.File(inode: 3));
        _files.AddLinkedName("/media/movies/Alien (1979) link.mkv", QuarantinedVideoPath);

        //Act
        var listed = new List<string>();
        await foreach (var entry in _files.ListMediaFilesAsync(Roots, cancellationToken))
            listed.Add(entry.AppPath);
        var underQuarantineRoot = new List<string>();
        await foreach (var entry in _files.ListMediaFilesAsync(["/media/.mam-quarantine"], cancellationToken))
            underQuarantineRoot.Add(entry.AppPath);

        //Assert
        Assert.Equal([VideoPath], listed);
        Assert.Empty(underQuarantineRoot);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The video is where it was, no rename was done and no folder made.</summary>
    private async Task AssertNothingMovedAsync(CancellationToken cancellationToken)
    {
        Assert.Equal(FileInventoryStatus.Found, (await _files.DescribeAsync(VideoPath, Roots, cancellationToken)).Status);
        Assert.Empty(_quarantine.Renames);
        Assert.Empty(_quarantine.CreatedFolders);
    }
}
