using System.Runtime.Versioning;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-9 — the inventory's look at real files in a temp folder: the allowed roots enforced folder by folder,
// what a file is (size, modification time, identity, link count), and every way a path can fail. Links have
// their own file, LocalFileInventoryLinksTests.
public sealed class LocalFileInventoryTests : IDisposable
{
    private readonly TemporaryFolder _folder = new("mam-file-inventory-");

    // Describe never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());

    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    /// <summary>For <c>SkipUnless</c>: Linux and Windows report a device and an inode.</summary>
    public static bool ReportsIdentity => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    /// <summary>For <c>SkipWhen</c>: Unix file modes don't exist on Windows.</summary>
    public static bool IsWindows => OperatingSystem.IsWindows();

    private char Separator => Path.DirectorySeparatorChar;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Capabilities_report_the_file_identity_on_linux_and_windows()
    {
        //Arrange
        var inventory = _inventory;

        //Act
        var capabilities = inventory.Capabilities;

        //Assert
        Assert.Equal(ReportsIdentity, capabilities.ReportsFileIdentity);
    }

    [Fact]
    public async Task Describe_a_file_inside_a_root_is_found_with_its_size_and_modification_time()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        var modified = new DateTime(2026, 9, 30, 12, 34, 56, 789, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, modified);

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(5, result.Value!.SizeBytes);
        Assert.Equal(modified, result.Value.ModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, result.Value.ModifiedUtc.Kind);
    }

    [Fact(Skip = "Only Linux and Windows report a file's identity.", SkipUnless = nameof(ReportsIdentity))]
    public async Task Describe_a_file_reports_its_device_its_own_inode_and_one_link()
    {
        //Arrange
        var path = _folder.WriteFile("first", "media", "first.mkv");
        var other = _folder.WriteFile("second", "media", "second.mkv");

        //Act
        var first = (await DescribeAsync(path, _media)).Value!;
        var second = (await DescribeAsync(other, _media)).Value!;

        //Assert
        Assert.NotNull(first.DeviceId);
        Assert.NotNull(first.Inode);
        Assert.Equal(1, first.LinkCount);
        // One folder, one device; two files, two inodes.
        Assert.Equal(first.DeviceId, second.DeviceId);
        Assert.NotEqual(first.Inode, second.Inode);
    }

    [Fact]
    public async Task Describe_with_no_allowed_root_reads_nothing()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");

        //Act
        var result = await DescribeAsync(path);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
        Assert.Equal("No media folder is allowed yet, so the app reads no file.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_with_only_roots_that_dont_parse_or_are_of_another_style_reads_nothing()
    {
        //Arrange
        // A root can only narrow what is read: one that isn't a usable path of this platform allows nothing.
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        var otherStyle = OperatingSystem.IsWindows() ? "/media" : @"D:\media";

        //Act
        var result = await DescribeAsync(path, "media", otherStyle, $"{_media}{Separator}..{Separator}media", "", " ");

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Equal("No media folder is allowed yet, so the app reads no file.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_ignores_an_unusable_root_next_to_a_usable_one()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");

        //Act
        var result = await DescribeAsync(path, "relative/media", _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
    }

    [Fact]
    public async Task Describe_a_file_outside_every_root_is_refused()
    {
        //Arrange
        var path = _folder.WriteFile("secret", "outside", "secret.txt");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"“{path}” is outside the media folders the app may read.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_a_file_in_a_sibling_folder_whose_name_starts_with_the_roots_is_refused()
    {
        //Arrange
        // media2 starts with the text "media" but is not inside it: roots are compared folder by folder.
        var path = _folder.WriteFile("12345", "media2", "movie.mkv");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
    }

    [Fact]
    public async Task Describe_a_relative_path_is_refused()
    {
        //Arrange
        _folder.WriteFile("12345", "media", "movie.mkv");

        //Act
        var result = await DescribeAsync($"media{Separator}movie.mkv", _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Equal($"“media{Separator}movie.mkv” is not a full path, so the app doesn't read it.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_a_path_climbing_out_with_dot_dot_is_refused_although_the_file_is_there()
    {
        //Arrange
        _folder.WriteFile("secret", "outside", "secret.txt");
        var path = $"{_media}{Separator}..{Separator}outside{Separator}secret.txt";

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Equal($"“{path}” contains . or .. as a folder name, so the app doesn't read it.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_a_missing_file_is_not_found()
    {
        //Arrange
        var path = Path.Combine(_media, "missing.mkv");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"There is no file at “{path}”.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_a_file_in_a_missing_folder_is_not_found()
    {
        //Arrange
        var path = Path.Combine(_media, "gone", "movie.mkv");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Describe_a_name_below_a_file_is_not_found()
    {
        //Arrange
        var file = _folder.WriteFile("12345", "media", "movie.mkv");

        //Act
        var result = await DescribeAsync(Path.Combine(file, "inside"), _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Describe_a_folder_is_not_a_regular_file()
    {
        //Arrange
        var path = _folder.CreateFolder("media", "Season 1");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotARegularFile, result.Status);
        Assert.Equal($"“{path}” is a folder, not a file.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_the_root_itself_is_not_a_regular_file()
    {
        //Arrange
        var root = _media;

        //Act
        var result = await DescribeAsync(root, root);

        //Assert
        Assert.Equal(FileInventoryStatus.NotARegularFile, result.Status);
    }

    [Fact]
    public async Task Describe_compares_letter_case_as_the_platforms_file_system_does()
    {
        //Arrange
        // NTFS ignores letter case and ext4 doesn't: on Linux /tmp/x/MEDIA is another folder than /tmp/x/media.
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        var root = Path.Combine(_folder.FullName, "MEDIA");

        //Act
        var result = await DescribeAsync(path, root);

        //Assert
        Assert.Equal(OperatingSystem.IsWindows() ? FileInventoryStatus.Found : FileInventoryStatus.OutsideAllowedRoots, result.Status);
    }

    [Fact(Skip = "Only Linux and Windows report a file's identity.", SkipUnless = nameof(ReportsIdentity))]
    public async Task Describe_a_hard_linked_file_reports_two_links_and_one_identity_under_both_names()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        var secondName = Path.Combine(_media, "the same movie.mkv");
        FileLinks.CreateHardLink(path, secondName);

        //Act
        var first = (await DescribeAsync(path, _media)).Value!;
        var second = (await DescribeAsync(secondName, _media)).Value!;

        //Assert
        // Phase B tells a hard link from a copy by exactly this.
        Assert.Equal(2, first.LinkCount);
        Assert.Equal(first, second);
    }

    [Fact(Skip = "Only Linux and Windows report a file's identity.", SkipUnless = nameof(ReportsIdentity))]
    public async Task Describe_after_a_rename_reports_the_same_identity()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        var before = (await DescribeAsync(path, _media)).Value!;
        var renamed = Path.Combine(_media, "renamed.mkv");
        File.Move(path, renamed);

        //Act
        var after = (await DescribeAsync(renamed, _media)).Value!;

        //Assert
        // The cache key survives a rename, so a renamed file is not read again.
        Assert.Equal(before, after);
    }

    [Fact(Skip = "Only Linux and Windows report a file's identity.", SkipUnless = nameof(ReportsIdentity))]
    public async Task Describe_after_a_new_file_was_moved_over_the_old_one_reports_a_new_identity()
    {
        //Arrange
        // How a download client or an editor replaces a file: write a new one, then move it over the name.
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        var before = (await DescribeAsync(path, _media)).Value!;
        var replacement = _folder.WriteFile("12345", "media", "movie.mkv.part");
        File.SetLastWriteTimeUtc(replacement, before.ModifiedUtc);
        File.Move(replacement, path, overwrite: true);

        //Act
        var after = (await DescribeAsync(path, _media)).Value!;

        //Assert
        // Same name, size and time, yet another file: only the inode tells, and the cache must not serve the
        // old file's streams for it.
        Assert.Equal((before.SizeBytes, before.ModifiedUtc, before.DeviceId), (after.SizeBytes, after.ModifiedUtc, after.DeviceId));
        Assert.NotEqual(before.Inode, after.Inode);
    }

    [Fact(Skip = "Unix file modes do not exist on Windows.", SkipWhen = nameof(IsWindows))]
    [UnsupportedOSPlatform("windows")]
    public async Task Describe_a_file_the_app_may_not_read_is_unreadable()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        File.SetUnixFileMode(path, UnixFileMode.None);
        if (CanStillRead(path))
            Assert.Skip("This process reads a file with mode 000 anyway (it runs as root), so it can't see the refusal.");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"The app isn't allowed to read “{path}”.", TestTexts.En(result.Error));
    }

    [Fact]
    public async Task Describe_cancelled_before_it_starts_throws()
    {
        //Arrange
        var path = _folder.WriteFile("12345", "media", "movie.mkv");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        var exception = await Record.ExceptionAsync(() => _inventory.DescribeAsync(path, [_media], cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    private Task<FileInventoryResult<FileFacts>> DescribeAsync(string path, params string[] roots) =>
        _inventory.DescribeAsync(path, roots, TestContext.Current.CancellationToken);

    private static bool CanStillRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
