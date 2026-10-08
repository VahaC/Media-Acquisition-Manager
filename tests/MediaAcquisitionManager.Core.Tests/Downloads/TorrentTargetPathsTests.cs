using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-38 — every place a client may write a torrent's file below a destination's folder, as the app never reads
// which content layout qBittorrent is set to: the torrent's own path (Original), a folder named after a lone file
// without its extension (Subfolder), and a torrent's files without its folder (NoSubfolder). An empty file is
// never a place: the client creates one only where nothing is.
public sealed class TorrentTargetPathsTests
{
    private static readonly NormalizedPath Movies = Parse("/media/movies");

    [Fact]
    public void For_one_file_is_its_name_and_a_folder_of_its_name_without_the_extension()
    {
        //Arrange
        var content = new TorrentContent(false, [File(["Film.2001.1080p.mkv"], 10)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content);

        //Assert
        Assert.Equal(
            ["/media/movies/Film.2001.1080p.mkv", "/media/movies/Film.2001.1080p/Film.2001.1080p.mkv"],
            targets.Select(target => target.Path.ToString()));
    }

    [Fact]
    public void For_a_torrent_with_a_folder_is_each_file_in_it_and_each_without_it()
    {
        //Arrange
        var content = new TorrentContent(true, [File(["Show", "S01E01.mkv"], 10), File(["Show", "Subs", "S01E01.srt"], 5)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content);

        //Assert
        Assert.Equal(
            ["/media/movies/Show/S01E01.mkv", "/media/movies/S01E01.mkv", "/media/movies/Show/Subs/S01E01.srt", "/media/movies/Subs/S01E01.srt"],
            targets.Select(target => target.Path.ToString()));
    }

    [Fact]
    public void For_leaves_empty_files_out_and_keeps_a_name_without_an_extension_whole()
    {
        //Arrange
        var content = new TorrentContent(false, [File(["README"], 3)]);
        var empty = new TorrentContent(true, [File(["Show", "empty.txt"], 0)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content);
        var none = TorrentTargetPaths.For(Movies, empty);

        //Assert
        Assert.Equal(["/media/movies/README", "/media/movies/README/README"], targets.Select(target => target.Path.ToString()));
        Assert.Empty(none);
    }

    private static TorrentContentFile File(string[] path, long length) => new(path, length, []);

    private static NormalizedPath Parse(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed : throw new ArgumentException(path, nameof(path));
}
