using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-26 — the quarantine on every platform (ADR 0030): the listing of a video's folder, which works everywhere so a
// development box shows what would move, gives its regular files with their facts and leaves out folders and links;
// and anywhere but Linux every write refuses before touching anything, as .NET has no rename that never replaces and
// never copies. The listing of every name of a folder, links leading nowhere among them, works everywhere too. The
// renames themselves are in LocalFileQuarantineLinuxTests.
public sealed class LocalFileQuarantineTests : IDisposable
{
    private const string NotLinux =
        "Only the app running on Linux renames or deletes a library file, as only there can a rename be made without ever replacing or copying one.";

    private readonly TemporaryFolder _folder = new("mam-quarantine-");

    // The look never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());
    private readonly LocalFileQuarantine _quarantine = new();
    private readonly string _media;

    public LocalFileQuarantineTests() => _media = _folder.CreateFolder("media");

    /// <summary>For <c>SkipWhen</c>.</summary>
    public static bool IsLinux => OperatingSystem.IsLinux();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Capabilities_can_rename_on_linux_alone()
    {
        //Arrange

        //Act
        var capabilities = _quarantine.Capabilities;

        //Assert
        Assert.Equal(OperatingSystem.IsLinux(), capabilities.CanRename);
    }

    [Fact]
    public async Task ListFolderFiles_gives_the_regular_files_beside_the_video_with_their_facts_and_leaves_out_folders_and_links()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "Alien (1979)", "Alien.mkv");
        var subtitles = _folder.WriteFile("In space", "media", "Alien (1979)", "Alien.uk.srt");
        var info = _folder.WriteFile("<movie/>", "media", "Alien (1979)", ".hidden.nfo");
        _folder.WriteFile("x", "media", "Alien (1979)", "Extras", "Alien.featurette.mkv");
        FileLinks.CreateFolderLink(_folder.PathOf("media", "Alien (1979)", "Alien.d"), _folder.PathOf("media", "Alien (1979)", "Extras"));
        _ = FileLinks.TryCreateSymbolicLink(_folder.PathOf("media", "Alien (1979)", "Alien.en.srt"), subtitles);

        //Act
        var listed = await _quarantine.ListFolderFilesAsync(film, [_media], cancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.Done, listed.Status);
        Assert.Null(listed.Error);
        Assert.Equal(
            [
                new FolderFile(".hidden.nfo", Canonical(info), await FactsOfAsync(info)),
                new FolderFile("Alien.mkv", Canonical(film), await FactsOfAsync(film)),
                new FolderFile("Alien.uk.srt", Canonical(subtitles), await FactsOfAsync(subtitles)),
            ],
            listed.Value);
        // The platform's own identity, where it has one: what check 6 matches a torrent's files by.
        Assert.All(listed.Value!, file => Assert.Equal(_inventory.Capabilities.ReportsFileIdentity, file.Facts.Inode is not null));
    }

    [Fact]
    public async Task ListFolderFiles_of_a_video_outside_the_allowed_roots_is_refused()
    {
        //Arrange
        var film = _folder.WriteFile("someone else's file", "outside", "Alien.mkv");

        //Act
        var listed = await _quarantine.ListFolderFilesAsync(film, [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileQuarantineStatus.OutsideAllowedRoots, listed.Status);
        Assert.Null(listed.Value);
        Assert.Equal($"“{Canonical(Path.GetDirectoryName(film)!)}” is outside the media folders the app may read.", listed.Error);
    }

    [Fact]
    public async Task ListFolderFiles_with_no_allowed_root_reads_nothing()
    {
        //Arrange
        var film = _folder.WriteFile("the old copy", "media", "Alien.mkv");

        //Act
        var listed = await _quarantine.ListFolderFilesAsync(film, [], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(
            new FileQuarantineResult<IReadOnlyList<FolderFile>>(
                FileQuarantineStatus.OutsideAllowedRoots, null, "No media folder is allowed yet, so the app reads no file."),
            listed);
    }

    [Fact]
    public async Task ListFolderNames_gives_every_name_in_the_folder_links_and_folders_too_and_a_folder_gone_is_not_found()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "Alien (1979)", "Alien.mkv");
        _folder.CreateFolder("media", "Alien (1979)", "Extras");
        // A link that leads nowhere: a look follows it and finds nothing, yet a rename back can't go where it is.
        var dangling = FileLinks.TryCreateSymbolicLink(_folder.PathOf("media", "Alien (1979)", "Alien.en.srt"), _folder.PathOf("media", "gone.srt"));
        var gone = _folder.PathOf("media", "Gone (2000)", "Gone.mkv");

        //Act
        var listed = await _quarantine.ListFolderNamesAsync(film, [_media], cancellationToken);
        var missing = await _quarantine.ListFolderNamesAsync(gone, [_media], cancellationToken);

        //Assert
        IReadOnlyList<string> expected = dangling ? ["Alien.en.srt", "Alien.mkv", "Extras"] : ["Alien.mkv", "Extras"];
        Assert.Equal(FileQuarantineStatus.Done, listed.Status);
        Assert.Equal(expected, listed.Value);
        Assert.Equal(
            new FileQuarantineResult<IReadOnlyList<string>>(
                FileQuarantineStatus.NotFound,
                null,
                $"The folder “{Canonical(Path.GetDirectoryName(gone)!)}” is gone, and the app creates no folder to put a file back."),
            missing);
    }

    [Fact(Skip = "Linux renames; LocalFileQuarantineLinuxTests covers it there.", SkipWhen = nameof(IsLinux))]
    public async Task Every_write_anywhere_but_linux_is_not_supported_and_changes_nothing()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var film = _folder.WriteFile("the old copy", "media", "Alien.mkv");
        var quarantined = _folder.WriteFile("a quarantined copy", "media", QuarantineFolder.Name, Guid.NewGuid().ToString(), "Old.mkv");
        var facts = await FactsOfAsync(film);
        var quarantinedFacts = await FactsOfAsync(quarantined);
        var quarantinePath = _folder.PathOf("media", QuarantineFolder.Name, Guid.NewGuid().ToString(), "Alien.mkv");

        //Act
        var moved = await _quarantine.MoveIntoQuarantineAsync(film, facts, quarantinePath, [_media], cancellationToken);
        var restored = await _quarantine.RestoreFromQuarantineAsync(quarantined, quarantinedFacts, _folder.PathOf("media", "Old.mkv"), [_media], cancellationToken);
        var deleted = await _quarantine.DeleteFromQuarantineAsync(quarantined, quarantinedFacts, [_media], cancellationToken);

        //Assert
        var refused = new FileQuarantineResult<FileFacts>(FileQuarantineStatus.NotSupported, null, NotLinux);
        Assert.Equal(refused, moved);
        Assert.Equal(refused, restored);
        Assert.Equal(refused, deleted);
        Assert.True(File.Exists(film));
        Assert.True(File.Exists(quarantined));
        Assert.False(File.Exists(quarantinePath));
        Assert.False(File.Exists(_folder.PathOf("media", "Old.mkv")));
    }

    [Fact(Skip = "Linux looks at the mounts; LocalFileQuarantinePlacesLinuxTests covers it there.", SkipWhen = nameof(IsLinux))]
    public async Task DescribeRenamePlaces_anywhere_but_linux_could_not_check_each_folder()
    {
        //Arrange
        var movies = _folder.CreateFolder("media", "movies");

        //Act
        var places = await _quarantine.DescribeRenamePlacesAsync([movies, _media], [_media], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal([movies, _media], places.Select(place => place.FolderAppPath));
        Assert.All(places, place => Assert.Equal([RenamePlaceProblem.CouldNotCheck], place.Problems));
    }

    private async Task<FileFacts> FactsOfAsync(string path) =>
        (await _inventory.DescribeAsync(path, [_media], TestContext.Current.CancellationToken)).Value
        ?? throw new InvalidOperationException($"“{path}” couldn't be described.");

    /// <summary>The path in the form the quarantine gives it (the drive letter in capitals on Windows).</summary>
    private static string Canonical(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed.ToString() : throw new ArgumentException($"“{path}” is not a full path.", nameof(path));
}
