using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-9 — links on the way to a file: followed while they stay inside the allowed roots, refused the moment
// one leads out (the "symlink escape" the ticket names), and never followed for ever. A link to a folder is a
// junction on Windows, which needs no privilege; a symbolic link to a file, or a relative one, is skipped on
// a Windows account that may not create one and runs on Linux in CI.
public sealed class LocalFileInventoryLinksTests : IDisposable
{
    private readonly TemporaryFolder _folder = new("mam-file-inventory-links-");

    // Describe never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());

    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryLinksTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Describe_through_a_folder_link_inside_the_root_is_found_with_the_targets_facts()
    {
        //Arrange
        var real = _folder.WriteFile("12345", "media", "real", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "linked"), Path.Combine(_media, "real"));
        var throughLink = Path.Combine(_media, "linked", "movie.mkv");

        //Act
        var result = await DescribeAsync(throughLink, _media);
        var direct = await DescribeAsync(real, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(direct.Value, result.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Describe_through_a_file_link_inside_the_root_is_found_with_the_targets_facts(bool relative)
    {
        //Arrange
        var real = _folder.WriteFile("12345", "media", "movie.mkv");
        var link = Path.Combine(_media, "alias.mkv");
        if (!FileLinks.TryCreateSymbolicLink(link, relative ? "movie.mkv" : real))
            Assert.Skip(FileLinks.SymbolicLinksUnavailable);

        //Act
        var result = await DescribeAsync(link, _media);
        var direct = await DescribeAsync(real, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        // The target's facts, not the link's own: its size, its inode, its link count.
        Assert.Equal(direct.Value, result.Value);
    }

    [Fact]
    public async Task Describe_through_a_folder_link_leading_outside_the_root_is_refused()
    {
        //Arrange
        var secret = _folder.WriteFile("secret", "outside", "secret.txt");
        FileLinks.CreateFolderLink(Path.Combine(_media, "escape"), _outside);
        var throughLink = Path.Combine(_media, "escape", "secret.txt");

        //Act
        var result = await DescribeAsync(throughLink, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(
            $"“{throughLink}” leads through a link to “{secret}”, outside the media folders the app may read.",
            result.Error);
    }

    [Fact]
    public async Task Describe_through_a_file_link_leading_outside_the_root_is_refused()
    {
        //Arrange
        var secret = _folder.WriteFile("secret", "outside", "secret.txt");
        var link = Path.Combine(_media, "movie.mkv");
        if (!FileLinks.TryCreateSymbolicLink(link, secret))
            Assert.Skip(FileLinks.SymbolicLinksUnavailable);

        //Act
        var result = await DescribeAsync(link, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Equal($"“{link}” leads through a link to “{secret}”, outside the media folders the app may read.", result.Error);
    }

    [Fact]
    public async Task Describe_through_a_relative_link_climbing_out_with_dot_dot_is_refused()
    {
        //Arrange
        _folder.WriteFile("secret", "outside", "secret.txt");
        _folder.CreateFolder("media", "movies");
        if (!FileLinks.TryCreateSymbolicLink(Path.Combine(_media, "movies", "up"), Path.Combine("..", "..", "outside"), toFolder: true))
            Assert.Skip(FileLinks.SymbolicLinksUnavailable);
        var throughLink = Path.Combine(_media, "movies", "up", "secret.txt");

        //Act
        var result = await DescribeAsync(throughLink, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
    }

    [Fact]
    public async Task Describe_through_a_relative_link_with_dot_dot_that_stays_inside_is_found()
    {
        //Arrange
        // movies/up -> ../shows: the .. steps back over the folders already resolved, not over the link.
        var real = _folder.WriteFile("12345", "media", "shows", "episode.mkv");
        _folder.CreateFolder("media", "movies");
        if (!FileLinks.TryCreateSymbolicLink(Path.Combine(_media, "movies", "up"), Path.Combine("..", "shows"), toFolder: true))
            Assert.Skip(FileLinks.SymbolicLinksUnavailable);

        //Act
        var result = await DescribeAsync(Path.Combine(_media, "movies", "up", "episode.mkv"), _media);
        var direct = await DescribeAsync(real, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(direct.Value, result.Value);
    }

    [Fact]
    public async Task Describe_through_a_loop_of_links_is_refused()
    {
        //Arrange
        var first = Path.Combine(_media, "loop-a");
        var second = Path.Combine(_media, "loop-b");
        FileLinks.CreateFolderLink(first, second);
        FileLinks.CreateFolderLink(second, first);
        var path = Path.Combine(first, "movie.mkv");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.Unreadable, result.Status);
        Assert.Equal($"“{path}” leads through more than 40 links, likely a loop, so the app doesn't follow them.", result.Error);
    }

    [Fact]
    public async Task Describe_through_a_link_to_nothing_is_not_found()
    {
        //Arrange
        FileLinks.CreateFolderLink(Path.Combine(_media, "gone"), _folder.PathOf("nowhere"));
        var path = Path.Combine(_media, "gone", "movie.mkv");

        //Act
        var result = await DescribeAsync(path, _media);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Describe_a_file_under_a_root_that_is_itself_a_link_is_found()
    {
        //Arrange
        // The owner chose /media, and /media is a link to the disk's own folder.
        var real = _folder.WriteFile("12345", "disk", "movies", "movie.mkv");
        var library = _folder.PathOf("library");
        FileLinks.CreateFolderLink(library, _folder.PathOf("disk", "movies"));

        //Act
        var result = await DescribeAsync(Path.Combine(library, "movie.mkv"), library);
        var direct = await DescribeAsync(real, _folder.PathOf("disk"));

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(direct.Value, result.Value);
    }

    [Fact]
    public async Task Describe_the_real_path_behind_a_root_that_is_a_link_is_outside_it()
    {
        //Arrange
        // A root allows the paths written under it; where it leads is not itself allowed by name.
        var real = _folder.WriteFile("12345", "disk", "movies", "movie.mkv");
        var library = _folder.PathOf("library");
        FileLinks.CreateFolderLink(library, _folder.PathOf("disk", "movies"));

        //Act
        var result = await DescribeAsync(real, library);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
    }

    [Fact]
    public async Task Describe_through_a_link_into_another_allowed_root_is_found()
    {
        //Arrange
        // /media/downloads -> /downloads, both allowed: two mounts, one library.
        var downloads = _folder.CreateFolder("downloads");
        _folder.WriteFile("12345", "downloads", "movie.mkv");
        FileLinks.CreateFolderLink(Path.Combine(_media, "downloads"), downloads);

        //Act
        var result = await DescribeAsync(Path.Combine(_media, "downloads", "movie.mkv"), _media, downloads);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
    }

    private Task<FileInventoryResult<FileFacts>> DescribeAsync(string path, params string[] roots) =>
        _inventory.DescribeAsync(path, roots, TestContext.Current.CancellationToken);
}
