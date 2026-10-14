using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-38 — every place a client may write a torrent's file below a destination's folder, as the app never reads
// which content layout qBittorrent is set to: the torrent's own path (Original), a folder named after a lone file
// without its extension (Subfolder), and a torrent's files without its folder (NoSubfolder). An empty file is
// never a place: the client creates one only where nothing is. MAM-30 — a client that writes the torrent's own layout
// only (Transmission) has its own paths alone, built from the names as the torrent holds them and read every way
// Transmission may clean them (4.0, 4.1 on Windows, 4.1 on Linux): a place per reading that names another, none for a
// reading under which a file's path is no name (that Transmission refuses the torrent), and no prediction at all when
// no reading places a file or its path isn't exact text (4.0 writes those bytes as they are). A file marked as padding
// is a place too, unless at BEP 47's own .pad/N: Transmission ignores the mark. With a partial-file suffix each place
// is also a target with it.
public sealed class TorrentTargetPathsTests
{
    private static readonly NormalizedPath Movies = Parse("/media/movies");

    [Fact]
    public void For_one_file_is_its_name_and_a_folder_of_its_name_without_the_extension()
    {
        //Arrange
        var content = new TorrentContent(false, [File(["Film.2001.1080p.mkv"], 10)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Equal(["/media/movies/Film.2001.1080p.mkv", "/media/movies/Film.2001.1080p/Film.2001.1080p.mkv"], PlacesOf(targets));
    }

    [Fact]
    public void For_a_torrent_with_a_folder_is_each_file_in_it_and_each_without_it()
    {
        //Arrange
        var content = new TorrentContent(true, [File(["Show", "S01E01.mkv"], 10), File(["Show", "Subs", "S01E01.srt"], 5)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Equal(
            ["/media/movies/Show/S01E01.mkv", "/media/movies/S01E01.mkv", "/media/movies/Show/Subs/S01E01.srt", "/media/movies/Subs/S01E01.srt"],
            PlacesOf(targets));
    }

    [Fact]
    public void For_leaves_empty_files_out_and_keeps_a_name_without_an_extension_whole()
    {
        //Arrange
        var content = new TorrentContent(false, [File(["README"], 3)]);
        var empty = new TorrentContent(true, [File(["Show", "empty.txt"], 0)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);
        var none = TorrentTargetPaths.For(Movies, empty, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Equal(["/media/movies/README", "/media/movies/README/README"], PlacesOf(targets));
        Assert.Empty(PlacesOf(none));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void For_any_of_three_layouts_without_a_suffix_is_every_layouts_place_alone(string? suffix)
    {
        //Arrange
        // qBittorrent's places, exactly as before MAM-30.
        var content = new TorrentContent(false, [File(["Film.2001.1080p.mkv"], 10)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, suffix);

        //Assert
        Assert.Equal(["/media/movies/Film.2001.1080p.mkv", "/media/movies/Film.2001.1080p/Film.2001.1080p.mkv"], PlacesOf(targets));
    }

    [Fact]
    public void For_any_of_three_layouts_with_a_suffix_suffixes_every_layouts_place()
    {
        //Arrange
        var content = new TorrentContent(false, [File(["Film.mkv"], 10)]);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, ".part");

        //Assert
        Assert.Equal(
            ["/media/movies/Film.mkv", "/media/movies/Film.mkv.part", "/media/movies/Film/Film.mkv", "/media/movies/Film/Film.mkv.part"],
            PlacesOf(targets));
    }

    [Fact]
    public void For_any_of_three_layouts_reads_libtorrents_cleaned_path_never_the_names_as_the_torrent_holds_them()
    {
        //Arrange
        // MAM-30 — the raw names are for a client that cleans names its own way; qBittorrent's places stay as before.
        var content = new TorrentContent(false, [new TorrentContentFile(["Film.mkv"], 10, [], ["A: B?/Film.mkv"])], "A: B?/Film.mkv");

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Equal(["/media/movies/Film.mkv", "/media/movies/Film/Film.mkv"], PlacesOf(targets));
    }

    [Fact]
    public void For_a_placement_this_build_does_not_know_throws()
    {
        //Arrange
        var content = new TorrentContent(false, [File(["Film.mkv"], 10)]);

        //Act
        var exception = Record.Exception(() => TorrentTargetPaths.For(Movies, content, (TorrentFilePlacement)99, null));

        //Assert
        // Never a guess at where an unknown client writes.
        Assert.IsType<ArgumentOutOfRangeException>(exception);
    }

    // ── own layout only (MAM-30) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void For_own_layout_only_of_one_file_is_its_name_in_the_folder_alone()
    {
        //Arrange
        // Transmission has no layout setting: a lone file lands directly in the folder, nowhere else.
        var content = One("Film.2001.1080p.mkv");

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(["/media/movies/Film.2001.1080p.mkv"], PlacesOf(targets));
    }

    [Fact]
    public void For_own_layout_only_of_a_torrent_with_a_folder_keeps_the_folder()
    {
        //Arrange
        var content = Several("Show", (["S01E01.mkv"], 10), (["Subs", "S01E01.srt"], 5));

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(["/media/movies/Show/S01E01.mkv", "/media/movies/Show/Subs/S01E01.srt"], PlacesOf(targets));
    }

    [Fact]
    public void For_own_layout_only_reads_the_names_as_the_torrent_holds_them_never_libtorrents_cleaned_path()
    {
        //Arrange
        // libtorrent drops the / of an element; Transmission makes it a folder boundary.
        var content = new TorrentContent(true, [new TorrentContentFile(["Show", "ab.mkv"], 10, [], ["a/b.mkv"])], "Show");

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(["/media/movies/Show/a/b.mkv"], PlacesOf(targets));
    }

    [Theory]
    // A / is a folder boundary, never a character of the file's name.
    [InlineData("x/y.mkv", "/media/movies/x/y.mkv")]
    // Each reading that names another place is a place: 4.0 and 4.1 on Windows strip the ends and replace the
    // characters a Windows name can't hold; 4.1 on Linux keeps the name.
    [InlineData("Film (2020).mkv ", "/media/movies/Film (2020).mkv|/media/movies/Film (2020).mkv ")]
    [InlineData("Film.mkv.", "/media/movies/Film.mkv|/media/movies/Film.mkv.")]
    [InlineData("A: B?.mkv", "/media/movies/A_ B_.mkv|/media/movies/A: B?.mkv")]
    [InlineData("CON.mkv", "/media/movies/_CON.mkv|/media/movies/CON.mkv")]
    // A control character stays in 4.0 and 4.1 on Linux, and is an underscore in 4.1 on Windows.
    [InlineData("a\u0001b.mkv", "/media/movies/a\u0001b.mkv|/media/movies/a_b.mkv")]
    // Every reading cuts the name at its first NUL.
    [InlineData("Film.mkv\0.exe", "/media/movies/Film.mkv")]
    // A name 4.0 cleans to nothing is a torrent it refuses; 4.1 on Linux writes it.
    [InlineData("  ", "/media/movies/  ")]
    [InlineData("...", "/media/movies/...")]
    [InlineData("..", "/media/movies/_..")]
    public void For_own_layout_only_of_one_file_is_a_place_for_each_reading_of_its_name_that_names_another(string name, string expected)
    {
        //Arrange
        var content = One(name);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(expected.Split('|'), PlacesOf(targets));
    }

    [Theory]
    [InlineData("Show", "/media/movies/Show/E01.mkv")]
    [InlineData("Show ", "/media/movies/Show/E01.mkv|/media/movies/Show /E01.mkv")]
    [InlineData("Show.", "/media/movies/Show/E01.mkv|/media/movies/Show./E01.mkv")]
    [InlineData("A: B?", "/media/movies/A_ B_/E01.mkv|/media/movies/A: B?/E01.mkv")]
    [InlineData("a/b", "/media/movies/a/b/E01.mkv")]
    [InlineData("Show\0hidden", "/media/movies/Show/E01.mkv")]
    // A name that cleans to nothing is no folder: the files land straight in the destination's folder.
    [InlineData("...", "/media/movies/E01.mkv|/media/movies/.../E01.mkv")]
    [InlineData("", "/media/movies/E01.mkv")]
    public void For_own_layout_only_of_several_files_reads_the_torrents_name_every_way_for_its_folder(string name, string expected)
    {
        //Arrange
        var content = Several(name, (["E01.mkv"], 10));

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(expected.Split('|'), PlacesOf(targets));
    }

    [Theory]
    // Each element of a file's path is read on its own, split on its own /.
    [InlineData("a/b.mkv", "/media/movies/Show/a/b.mkv")]
    [InlineData("Season 1|E01.mkv", "/media/movies/Show/Season 1/E01.mkv")]
    [InlineData(" Season 1 |E01.mkv", "/media/movies/Show/Season 1/E01.mkv|/media/movies/Show/ Season 1 /E01.mkv")]
    [InlineData("CON|E01.mkv", "/media/movies/Show/_CON/E01.mkv|/media/movies/Show/CON/E01.mkv")]
    [InlineData("E01.mkv\0.exe", "/media/movies/Show/E01.mkv")]
    // An element that reads as nothing is no folder: a//b is a/b on disk.
    [InlineData("...|E01.mkv", "/media/movies/Show/E01.mkv|/media/movies/Show/.../E01.mkv")]
    [InlineData("..|E01.mkv", "/media/movies/Show/E01.mkv|/media/movies/Show/_../E01.mkv")]
    [InlineData("|E01.mkv", "/media/movies/Show/E01.mkv")]
    // A path 4.0 reads as nothing is a torrent it refuses ("invalid path"): no place from it.
    [InlineData("...", "/media/movies/Show/...")]
    public void For_own_layout_only_reads_each_element_of_a_files_path_on_its_own(string elements, string expected)
    {
        //Arrange
        var content = Several("Show", (elements.Split('|'), 10));

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(expected.Split('|'), PlacesOf(targets));
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "/")]
    [InlineData(false, "\0Film.mkv")]
    [InlineData(true, "")]
    [InlineData(true, "/")]
    [InlineData(true, "\0E01.mkv")]
    [InlineData(true, "|/")]
    public void For_own_layout_only_of_a_file_no_reading_places_can_not_predict_the_content(bool several, string raw)
    {
        //Arrange
        // Every Transmission refuses such a torrent, or writes where no reading says: the caller treats the release as
        // one whose list of files it can't read, never as one with nothing to check.
        var content = several ? Several("Show", (["E01.mkv"], 10), (raw.Split('|'), 5)) : One(raw);

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");

        //Assert
        Assert.Null(targets);
    }

    [Fact]
    public void For_own_layout_only_without_the_names_as_the_torrent_holds_them_can_not_predict_the_content()
    {
        //Arrange
        // Content not read from a torrent's bytes: libtorrent's cleaned path says nothing of where Transmission writes.
        var noRawPath = new TorrentContent(false, [File(["Film.mkv"], 10)], "Film.mkv");
        var noRawName = new TorrentContent(true, [new TorrentContentFile(["Show", "E01.mkv"], 10, [], ["E01.mkv"])]);

        //Act
        var withoutPath = TorrentTargetPaths.For(Movies, noRawPath, TorrentFilePlacement.OwnLayoutOnly, null);
        var withoutName = TorrentTargetPaths.For(Movies, noRawName, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Null(withoutPath);
        Assert.Null(withoutName);
    }

    [Fact]
    public void For_own_layout_only_of_a_file_whose_path_is_not_exact_text_can_not_predict_the_content()
    {
        //Arrange
        // Transmission 4.0 writes such a path's bytes as they are, which no path in the app names: a place the check
        // can't look at, never one it may call free. An empty file, never a place, changes nothing; nor does a pad file
        // at BEP 47's own name.
        var notExact = new TorrentContent(
            true, [new TorrentContentFile(["Show", "_ 1", "E01.mkv"], 10, [], ["� 1", "E01.mkv"], RawPathIsExact: false)], "Show");
        var padNotExact = new TorrentContent(
            true, [], "Show", [new TorrentContentFile(["Show", ".pad", "16"], 16, [], [".pad", "16"], RawPathIsExact: false)]);
        var emptyNotExact = new TorrentContent(
            true,
            [new TorrentContentFile(["Show", "E01.mkv"], 10, [], ["E01.mkv"]), new TorrentContentFile(["Show", "_"], 0, [], ["�"], RawPathIsExact: false)],
            "Show");

        //Act
        var ownLayout = TorrentTargetPaths.For(Movies, notExact, TorrentFilePlacement.OwnLayoutOnly, ".part");
        var padOwnLayout = TorrentTargetPaths.For(Movies, padNotExact, TorrentFilePlacement.OwnLayoutOnly, ".part");
        var emptyOwnLayout = TorrentTargetPaths.For(Movies, emptyNotExact, TorrentFilePlacement.OwnLayoutOnly, null);
        var anyLayout = TorrentTargetPaths.For(Movies, notExact, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Null(ownLayout);
        Assert.Null(padOwnLayout);
        Assert.Equal(["/media/movies/Show/E01.mkv"], PlacesOf(emptyOwnLayout));
        // qBittorrent's places read libtorrent's cleaned names, as before.
        Assert.Equal(["/media/movies/Show/_ 1/E01.mkv", "/media/movies/_ 1/E01.mkv"], PlacesOf(anyLayout));
    }

    [Theory]
    // A name BEP 47 gives a pad file lands in a .pad folder, where no library keeps a file, whatever it holds.
    [InlineData(".pad|16", "")]
    // Any other is written there like any file, as Transmission ignores the mark.
    [InlineData("S01E01.mkv", "/media/movies/Show/S01E01.mkv|/media/movies/Show/S01E01.mkv.part")]
    [InlineData(".pad|x", "/media/movies/Show/.pad/x|/media/movies/Show/.pad/x.part")]
    [InlineData(".pad|1|2", "/media/movies/Show/.pad/1/2|/media/movies/Show/.pad/1/2.part")]
    public void For_own_layout_only_places_a_file_marked_as_padding_like_any_other_but_at_bep47s_own_name(string rawPath, string expected)
    {
        //Arrange
        var pad = new TorrentContentFile(["Show", .. rawPath.Split('|')], 16, [], rawPath.Split('|'));
        var content = new TorrentContent(true, [], "Show", [pad]);

        //Act
        var ownLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");
        var anyLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, ".part");

        //Assert
        Assert.Equal(expected.Split('|', StringSplitOptions.RemoveEmptyEntries), PlacesOf(ownLayout));
        Assert.All(ownLayout!, target => Assert.Same(pad, target.File));
        // libtorrent never writes a pad file: never one of qBittorrent's places.
        Assert.Empty(PlacesOf(anyLayout));
    }

    [Fact]
    public void For_with_a_partial_file_suffix_adds_each_files_suffixed_place_for_the_same_file()
    {
        //Arrange
        // An unfinished file is <name>.part on disk, and 4.1 renames a file already there that doesn't verify to that
        // name: the place is a target too, of the same file, so a collision there isn't counted as another file.
        var content = Several("Show", (["S01E01.mkv"], 10), (["Subs", "S01E01.srt"], 5));

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");

        //Assert
        Assert.NotNull(targets);
        Assert.Equal(
            [
                "/media/movies/Show/S01E01.mkv",
                "/media/movies/Show/S01E01.mkv.part",
                "/media/movies/Show/Subs/S01E01.srt",
                "/media/movies/Show/Subs/S01E01.srt.part",
            ],
            PlacesOf(targets));
        Assert.Equal([content.Files[0], content.Files[0], content.Files[1], content.Files[1]], targets.Select(target => target.File));
        Assert.Same(targets[0].File, targets[1].File);
    }

    [Fact]
    public void For_with_a_partial_file_suffix_follows_each_readings_place_with_its_suffixed_place_all_for_the_same_file()
    {
        //Arrange
        var content = One("A: B?.mkv");

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");

        //Assert
        Assert.NotNull(targets);
        Assert.Equal(
            ["/media/movies/A_ B_.mkv", "/media/movies/A_ B_.mkv.part", "/media/movies/A: B?.mkv", "/media/movies/A: B?.mkv.part"],
            PlacesOf(targets));
        Assert.All(targets, target => Assert.Same(content.Files[0], target.File));
    }

    [Fact]
    public void For_own_layout_only_leaves_empty_files_and_their_suffixed_place_out()
    {
        //Arrange
        // An empty file is never a place, so one whose path reads as nothing leaves the rest predictable.
        var content = Several("Show", (["empty.txt"], 0), (["S01E01.mkv"], 10), ([""], 0));

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");

        //Assert
        Assert.Equal(["/media/movies/Show/S01E01.mkv", "/media/movies/Show/S01E01.mkv.part"], PlacesOf(targets));
    }

    // ── own layout only, from a torrent's bytes ───────────────────────────────────────────────────

    [Fact]
    public void For_own_layout_only_of_a_read_torrent_places_each_file_every_way_transmission_may_write_it()
    {
        //Arrange
        var torrent = TestTorrent.V1(
            "A: B? ",
            [(["CON", "x/y.mkv"], TestTorrent.Bytes(10, seed: 1)), (["Subs", "y.srt"], TestTorrent.Bytes(5, seed: 2))]);
        var content = TorrentContentReader.Read(torrent)!;

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(
            [
                "/media/movies/A_ B_/_CON/x/y.mkv",
                "/media/movies/A: B? /CON/x/y.mkv",
                "/media/movies/A_ B_/Subs/y.srt",
                "/media/movies/A: B? /Subs/y.srt",
            ],
            PlacesOf(targets));
    }

    [Fact]
    public void For_own_layout_only_of_a_read_torrent_with_pad_files_leaves_them_out()
    {
        //Arrange
        // Each pad file here has BEP 47's own name, .pad/N: a .pad folder, where no library keeps a file.
        var torrent = TestTorrent.V1(
            "Show", [(["E01.mkv"], TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 10, seed: 3)), (["E01.srt"], TestTorrent.Bytes(100, seed: 4))], padFiles: true);
        var content = TorrentContentReader.Read(torrent)!;

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");

        //Assert
        Assert.Equal(
            ["/media/movies/Show/E01.mkv", "/media/movies/Show/E01.mkv.part", "/media/movies/Show/E01.srt", "/media/movies/Show/E01.srt.part"],
            PlacesOf(targets));
    }

    [Fact]
    public void For_own_layout_only_of_a_read_v2_torrent_places_its_files_by_their_file_tree_keys()
    {
        //Arrange
        var torrent = TestTorrent.V2("Pack", [(["S01 ", "E01.mkv"], TestTorrent.Bytes(10, seed: 5)), (["S01 ", "E01.srt"], TestTorrent.Bytes(20, seed: 6))]);
        var content = TorrentContentReader.Read(torrent)!;

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(
            ["/media/movies/Pack/S01/E01.mkv", "/media/movies/Pack/S01 /E01.mkv", "/media/movies/Pack/S01/E01.srt", "/media/movies/Pack/S01 /E01.srt"],
            PlacesOf(targets));
    }

    [Fact]
    public void For_a_read_torrent_whose_name_holds_a_nul_places_it_where_each_client_cuts_it()
    {
        //Arrange
        // libtorrent drops the NUL and keeps the rest; Transmission cuts the name there.
        var content = TorrentContentReader.Read(TestTorrent.V1("Film.mkv\0.exe", TestTorrent.Bytes(10, seed: 7)))!;

        //Act
        var anyLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);
        var ownLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(["/media/movies/Film.mkv.exe", "/media/movies/Film.mkv/Film.mkv.exe"], PlacesOf(anyLayout));
        Assert.Equal(["/media/movies/Film.mkv"], PlacesOf(ownLayout));
    }

    [Fact]
    public void For_own_layout_only_of_a_read_torrent_whose_name_starts_with_a_nul_can_not_predict_it()
    {
        //Arrange
        // libtorrent reads "Film.mkv"; every Transmission reads no name at all.
        var content = TorrentContentReader.Read(TestTorrent.V1("\0Film.mkv", TestTorrent.Bytes(10, seed: 8)))!;

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Null(targets);
    }

    [Theory]
    // Transmission 4.0 cleans each element with its NUL, and the system cuts the whole path there: Show/a here, a file
    // where every reading sees a folder...
    [InlineData("a\0b|c.mkv")]
    // ...and Show/E01_ here, its space kept as no reading keeps it.
    [InlineData("E01: \0.mkv")]
    public void For_own_layout_only_of_a_read_torrent_with_a_nul_in_a_files_path_can_not_predict_it(string elements)
    {
        //Arrange
        var content = TorrentContentReader.Read(TestTorrent.V1("Show", [(elements.Split('|'), TestTorrent.Bytes(10, seed: 9))]))!;

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");

        //Assert
        Assert.Null(targets);
    }

    [Fact]
    public void For_own_layout_only_of_a_read_torrent_whose_path_is_not_utf8_can_not_predict_it_while_qbittorrents_places_stay()
    {
        //Arrange
        // A Windows-1251 path ("Сезон 1") with no path.utf-8 beside it: 4.1 writes U+FFFD for it, 4.0 the bytes as they
        // are, which no path in the app names.
        var bytes = TestTorrent.Bytes(10, seed: 10);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>
            {
                TestTorrent.Dictionary(("length", 10L), ("path", new List<object> { new byte[] { 0xD1, 0xE5, 0xE7, 0xEE, 0xED, 0x20, 0x31 }, "E01.mkv" })),
            }),
            ("name", "Show"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", SHA1.HashData(bytes))))));
        var content = TorrentContentReader.Read(torrent)!;

        //Act
        var ownLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");
        var anyLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Null(ownLayout);
        Assert.Equal(2, PlacesOf(anyLayout).Length);
    }

    [Fact]
    public void For_own_layout_only_of_a_read_torrent_that_marks_a_media_file_as_padding_still_places_it()
    {
        //Arrange
        // The mark is the torrent's word alone: Transmission writes the file at its path with the bytes its pieces give,
        // and 4.1 renames a file already there that doesn't verify. libtorrent never writes it.
        var subtitles = TestTorrent.Bytes(10, seed: 11);
        var episode = TestTorrent.Bytes(20, seed: 12);
        byte[] data = [.. subtitles, .. episode];
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>
            {
                TestTorrent.Dictionary(("length", 10L), ("path", new List<object> { "S01E01.srt" })),
                TestTorrent.Dictionary(("attr", "p"), ("length", 20L), ("path", new List<object> { "S01E01.mkv" })),
            }),
            ("name", "Show"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", SHA1.HashData(data))))));
        var content = TorrentContentReader.Read(torrent)!;

        //Act
        var ownLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, ".part");
        var anyLayout = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.AnyOfThreeLayouts, null);

        //Assert
        Assert.Equal(
            [
                "/media/movies/Show/S01E01.srt",
                "/media/movies/Show/S01E01.srt.part",
                "/media/movies/Show/S01E01.mkv",
                "/media/movies/Show/S01E01.mkv.part",
            ],
            PlacesOf(ownLayout));
        Assert.Equal(20, ownLayout![2].File.Length);
        Assert.Equal(["/media/movies/Show/S01E01.srt", "/media/movies/S01E01.srt"], PlacesOf(anyLayout));
    }

    [Fact]
    public void For_own_layout_only_of_a_read_hybrid_torrent_places_it_by_its_v1_name_never_its_file_tree_key()
    {
        //Arrange
        // Transmission ignores the file tree ("'file tree' is ignored", 4.0.6 and 4.1.3) and writes the v1 name.
        var bytes = TestTorrent.Bytes(10, seed: 13);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                ("length", 10L), ("pieces root", new byte[32]))))))),
            ("length", 10L),
            ("meta version", 2L),
            ("name", "x: y.mkv"),
            ("piece length", (long)TestTorrent.PieceLength),
            ("pieces", SHA1.HashData(bytes))))));
        var content = TorrentContentReader.Read(torrent)!;

        //Act
        var targets = TorrentTargetPaths.For(Movies, content, TorrentFilePlacement.OwnLayoutOnly, null);

        //Assert
        Assert.Equal(["/media/movies/x_ y.mkv", "/media/movies/x: y.mkv"], PlacesOf(targets));
    }

    private static TorrentContentFile File(string[] path, long length) => new(path, length, []);

    /// <summary>A torrent of one file as the reader gives it, named <paramref name="name"/> as the torrent holds it.</summary>
    private static TorrentContent One(string name) => new(false, [new TorrentContentFile([name], 10, [], [name])], name);

    /// <summary>A torrent of several files under <paramref name="name"/> as the reader gives it, each path below it as the
    /// torrent holds it (the cleaned path isn't what <see cref="TorrentFilePlacement.OwnLayoutOnly"/> reads).</summary>
    private static TorrentContent Several(string name, params (string[] RawPath, long Length)[] files) =>
        new(true, [.. files.Select(file => new TorrentContentFile([name, .. file.RawPath], file.Length, [], file.RawPath))], name);

    private static string[] PlacesOf(IReadOnlyList<TorrentTarget>? targets)
    {
        Assert.NotNull(targets);
        return [.. targets.Select(target => target.Path.ToString())];
    }

    private static NormalizedPath Parse(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed : throw new ArgumentException(path, nameof(path));
}
