using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-43 — the walk of one folder, what lists a Blu-ray or DVD folder a library reports as one version: only what lies
// under the folder, over real folders in a temp folder; a folder outside the allowed roots, or a link leading out of
// them, refused before anything is listed; a folder that is away answered as unlisted; a link inside the folder
// leading out of it refused, as out of a root. A link to a folder is a junction on Windows, which needs no privilege.
public sealed class LocalFileInventoryFolderListingTests : IDisposable
{
    private readonly TemporaryFolder _folder = new("mam-folder-listing-");

    // The walk never runs ffprobe.
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());

    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryFolderListingTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ListUnder_gives_the_media_files_under_the_folder_and_nothing_beside_it()
    {
        //Arrange
        _folder.WriteFile("1", "media", "movies", "DIRTY_DOZEN_1967_RMX", "BDMV", "STREAM", "00000.m2ts");
        _folder.WriteFile("2", "media", "movies", "DIRTY_DOZEN_1967_RMX", "BDMV", "STREAM", "00001.m2ts");
        _folder.WriteFile("3", "media", "movies", "DIRTY_DOZEN_1967_RMX", "BDMV", "index.bdmv");
        _folder.WriteFile("4", "media", "movies", "Other (2001).mkv");
        var disc = _folder.PathOf("media", "movies", "DIRTY_DOZEN_1967_RMX");

        //Act
        var entries = await ListUnderAsync(disc, _media);

        //Assert
        Assert.Equal([Media(disc, "BDMV", "STREAM", "00000.m2ts"), Media(disc, "BDMV", "STREAM", "00001.m2ts")], entries);
    }

    [Fact]
    public async Task ListUnder_refuses_a_folder_outside_the_allowed_roots_without_listing_it()
    {
        //Arrange
        _folder.WriteFile("secret", "outside", "BDMV", "STREAM", "00000.m2ts");

        //Act
        var entries = await ListUnderAsync(_outside, _media);

        //Assert
        Assert.Equal(
            [Problem(FileListingStatus.Skipped, _outside, $"“{Canonical(_outside)}” is outside the media folders the app may read.")],
            entries);
    }

    [Fact]
    public async Task ListUnder_refuses_a_folder_that_is_a_link_leading_out_of_the_roots()
    {
        //Arrange
        _folder.WriteFile("secret", "outside", "BDMV", "STREAM", "00000.m2ts");
        var link = Path.Combine(_media, "disc");
        FileLinks.CreateFolderLink(link, _outside);

        //Act
        var entries = await ListUnderAsync(link, _media);

        //Assert
        Assert.Equal(
            [
                Problem(
                    FileListingStatus.Skipped,
                    link,
                    $"“{Canonical(link)}” leads through a link to “{Canonical(_outside)}”, outside the media folders the app may read."),
            ],
            entries);
    }

    [Fact]
    public async Task ListUnder_answers_a_missing_folder_as_unlisted()
    {
        //Arrange
        var missing = _folder.PathOf("media", "gone");

        //Act
        var entries = await ListUnderAsync(missing, _media);

        //Assert
        Assert.Equal([Problem(FileListingStatus.Unlisted, missing, $"There is no file at “{Canonical(missing)}”.")], entries);
    }

    [Fact]
    public async Task ListUnder_refuses_a_link_inside_the_folder_that_leads_out_of_it_even_into_an_allowed_root()
    {
        //Arrange
        // The folder is the walk's only root: a link to elsewhere in the media folders is not the disc's.
        _folder.WriteFile("1", "media", "disc", "BDMV", "STREAM", "00000.m2ts");
        _folder.WriteFile("2", "media", "elsewhere", "film.mkv");
        var disc = _folder.PathOf("media", "disc");
        var link = Path.Combine(disc, "BDMV", "more");
        FileLinks.CreateFolderLink(link, _folder.PathOf("media", "elsewhere"));

        //Act
        var entries = await ListUnderAsync(disc, _media);

        //Assert
        Assert.Equal(FileListingStatus.Skipped, Assert.Single(entries, entry => entry.AppPath == Canonical(link)).Status);
        Assert.Equal(Media(disc, "BDMV", "STREAM", "00000.m2ts"), Assert.Single(entries, entry => entry.Status == FileListingStatus.MediaFile));
    }

    [Fact]
    public async Task ListUnder_with_no_allowed_root_refuses_the_folder()
    {
        //Arrange
        _folder.WriteFile("1", "media", "disc", "BDMV", "STREAM", "00000.m2ts");
        var disc = _folder.PathOf("media", "disc");

        //Act
        var entries = await ListUnderAsync(disc);

        //Assert
        Assert.Equal([Problem(FileListingStatus.Skipped, disc, "No media folder is allowed yet, so the app reads no file.")], entries);
    }

    [Fact]
    public async Task ListUnder_cancelled_before_it_starts_throws()
    {
        //Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        //Act
        var walk = async () =>
        {
            await foreach (var _ in _inventory.ListMediaFilesUnderAsync(_media, [_media], cancellation.Token))
            {
            }
        };

        //Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(walk);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private async Task<List<FileListingEntry>> ListUnderAsync(string folder, params string[] roots)
    {
        var entries = new List<FileListingEntry>();
        await foreach (var entry in _inventory.ListMediaFilesUnderAsync(folder, roots, TestContext.Current.CancellationToken))
            entries.Add(entry);
        return entries;
    }

    /// <summary>A media file name reached by its own path: where it is, is where its name says.</summary>
    private static FileListingEntry Media(string folder, params string[] names)
    {
        var path = Canonical(Path.Combine([folder, .. names]));
        return new FileListingEntry(FileListingStatus.MediaFile, path, path, null);
    }

    private static FileListingEntry Problem(FileListingStatus status, string appPath, string error) =>
        new(status, Canonical(appPath), null, error);

    /// <summary>The path in the form the walk gives it (the drive letter in capitals on Windows).</summary>
    private static string Canonical(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _)
            ? parsed.ToString()
            : throw new ArgumentException($"“{path}” is not a full path.", nameof(path));
}
