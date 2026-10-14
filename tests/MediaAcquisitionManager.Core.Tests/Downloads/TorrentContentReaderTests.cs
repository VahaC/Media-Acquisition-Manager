using System.Globalization;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-38 — which files a .torrent will write, how long each is, and the torrent's hashes of up to three pieces
// wholly inside each (ADR 0034): v1 from `files` or `length` and `pieces`, pad files left out but counted where the
// pieces lie, the UTF-8 names first; a hybrid by its v1 pieces; v2 from its file tree and piece layers, its trees as
// wide as BEP 52 builds them. Names as libtorrent 2.0 makes them: every path element and tree key kept as one
// element, the folder `_` or the v1 info hash when the name is empty or dropped. The torrents are built from real
// bytes (TestTorrent), so each expected hash is the one the bytes have. A file from a tracker is hostile until read:
// anything malformed, or refused by libtorrent, is no content, never an exception. MAM-30 — beside the cleaned names,
// the torrent's name and each file's path as the torrent holds them, read as Transmission reads text: cut at the first
// NUL, one U+FFFD for each sequence that isn't UTF-8 as utf8cpp's replace_invalid marks it; a file's path marked when
// that reading changed its bytes (Transmission 4.0 writes path elements unread); the files marked as padding that have a
// path kept apart (Transmission ignores the mark); a hybrid's names its v1 ones (Transmission ignores the file tree).
public sealed class TorrentContentReaderTests
{
    private const int Piece = TestTorrent.PieceLength;

    [Fact]
    public void Read_of_a_v1_torrent_of_one_file_is_its_name_and_length_and_its_first_middle_and_last_pieces()
    {
        //Arrange
        var content = TestTorrent.Bytes((5 * Piece) + 100, seed: 1);
        var torrent = TestTorrent.V1("Film.2001.mkv", content);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.NotNull(read);
        Assert.False(read.HasRootFolder);
        var file = Assert.Single(read.Files);
        Assert.Equal(["Film.2001.mkv"], file.Path);
        Assert.Equal(content.Length, file.Length);
        Assert.Equal(
            [(0L, Piece), (2L * Piece, Piece), (5L * Piece, 100)],
            file.Samples.Select(sample => (sample.Range.Offset, sample.Range.Length)));
        Assert.All(file.Samples, sample => Assert.Equal(Sha1Of(content, sample.Range), sample.ExpectedHash));
        Assert.All(file.Samples, sample => Assert.Equal(FilePieceHashing.Sha1, sample.Range.Hashing));
    }

    [Fact]
    public void Read_of_a_v1_torrent_of_several_files_compares_only_pieces_wholly_inside_a_file()
    {
        //Arrange
        var first = TestTorrent.Bytes((3 * Piece) + 10, seed: 2);
        var second = TestTorrent.Bytes(100, seed: 3);
        var torrent = TestTorrent.V1("Show", [(["S01E01.mkv"], first), (["Subs", "S01E01.srt"], second)]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        // The fourth piece holds the first file's tail and the whole second file, so it tells of neither.
        Assert.NotNull(read);
        Assert.True(read.HasRootFolder);
        Assert.Equal([["Show", "S01E01.mkv"], ["Show", "Subs", "S01E01.srt"]], read.Files.Select(file => file.Path));
        Assert.Equal([0L, Piece, 2L * Piece], read.Files[0].Samples.Select(sample => sample.Range.Offset));
        Assert.Empty(read.Files[1].Samples);
    }

    [Fact]
    public void Read_of_a_v1_torrent_with_pad_files_leaves_them_out_and_compares_the_aligned_file_after_them()
    {
        //Arrange
        var first = TestTorrent.Bytes((3 * Piece) + 10, seed: 2);
        var second = TestTorrent.Bytes(100, seed: 3);
        var torrent = TestTorrent.V1("Show", [(["S01E01.mkv"], first), (["S01E01.srt"], second)], padFiles: true);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.NotNull(read);
        Assert.Equal([["Show", "S01E01.mkv"], ["Show", "S01E01.srt"]], read.Files.Select(file => file.Path));
        Assert.Equal([["S01E01.mkv"], ["S01E01.srt"]], read.Files.Select(file => file.RawPath!));
        var sample = Assert.Single(read.Files[1].Samples);
        Assert.Equal((0L, 100), (sample.Range.Offset, sample.Range.Length));
        Assert.Equal(SHA1.HashData(second), sample.ExpectedHash);
    }

    [Fact]
    public void Read_keeps_each_file_marked_as_padding_that_has_a_path_apart_read_as_a_real_file_is()
    {
        //Arrange
        // MAM-30 — libtorrent never writes one, but Transmission ignores the mark and writes it at its path. One without
        // a path Transmission never writes either: it refuses such a torrent ("invalid path").
        var first = TestTorrent.Bytes((3 * Piece) + 10, seed: 2);
        var second = TestTorrent.Bytes(100, seed: 3);
        var gap = Piece - 10;
        var padded = TestTorrent.V1("Show", [(["S01E01.mkv"], first), (["S01E01.srt"], second)], padFiles: true);
        byte[] data = [.. first, .. new byte[gap], .. second];
        var pathless = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>
            {
                TestTorrent.Dictionary(("length", (long)first.Length), ("path", new List<object> { "S01E01.mkv" })),
                TestTorrent.Dictionary(("attr", "p"), ("length", (long)gap)),
                TestTorrent.Dictionary(("length", (long)second.Length), ("path", new List<object> { "S01E01.srt" })),
            }),
            ("name", "Show"),
            ("piece length", (long)Piece),
            ("pieces", data.Chunk(Piece).SelectMany(piece => SHA1.HashData(piece)).ToArray())))));

        //Act
        var readPadded = TorrentContentReader.Read(padded);
        var readPathless = TorrentContentReader.Read(pathless);

        //Assert
        Assert.Equal([["Show", "S01E01.mkv"], ["Show", "S01E01.srt"]], readPadded!.Files.Select(file => file.Path));
        var pad = Assert.Single(readPadded.PadFiles!);
        Assert.Equal([".pad", gap.ToString(CultureInfo.InvariantCulture)], pad.RawPath);
        Assert.True(pad.RawPathIsExact);
        Assert.Equal(gap, pad.Length);
        Assert.Equal(["Show", ".pad", gap.ToString(CultureInfo.InvariantCulture)], pad.Path);
        Assert.Equal([["Show", "S01E01.mkv"], ["Show", "S01E01.srt"]], readPathless!.Files.Select(file => file.Path));
        Assert.Empty(readPathless.PadFiles!);
    }

    [Fact]
    public void Read_takes_the_utf8_names_before_the_plain_ones_as_libtorrent_does()
    {
        //Arrange
        var content = TestTorrent.Bytes(10, seed: 4);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 10L), ("name", "Nie dobre"), ("name.utf-8", "Добрий фільм.mkv"), ("piece length", (long)Piece), ("pieces", SHA1.HashData(content))))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal(["Добрий фільм.mkv"], Assert.Single(read!.Files).Path);
    }

    [Fact]
    public void Read_cleans_every_path_element_and_makes_a_dot_folder_an_underscore_as_libtorrent_forces_it()
    {
        //Arrange
        var content = TestTorrent.Bytes(10, seed: 5);
        var torrent = TestTorrent.V1("Show", [(["..", ".", "", "Sea\u0001son 1", "a/b.mkv"], content)]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal(["Show", "_", "_", "_", "Sea_son 1", "ab.mkv"], Assert.Single(read!.Files).Path);
    }

    [Fact]
    public void Read_keeps_the_name_and_each_files_path_as_the_torrent_holds_them_beside_the_cleaned_ones()
    {
        //Arrange
        // MAM-30 — for a client that cleans names its own way: never cleaned, and each element kept apart.
        var content = TestTorrent.Bytes(10, seed: 5);
        var torrent = TestTorrent.V1("Show: 1 ", [(["..", ".", "", "Sea\u0001son 1", "a/b.mkv"], content)]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        var file = Assert.Single(read!.Files);
        Assert.Equal(["Show: 1 ", "_", "_", "_", "Sea_son 1", "ab.mkv"], file.Path);
        Assert.Equal("Show: 1 ", read.RawName);
        Assert.Equal(["..", ".", "", "Sea\u0001son 1", "a/b.mkv"], file.RawPath);
    }

    [Fact]
    public void Read_of_a_torrent_of_one_file_keeps_its_name_as_the_files_path_as_the_torrent_holds_it()
    {
        //Arrange
        var torrent = TestTorrent.V1("x/y.mkv", TestTorrent.Bytes(10, seed: 1));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        // libtorrent drops the /; a client that splits on it reads a folder and a file.
        var file = Assert.Single(read!.Files);
        Assert.Equal(["xy.mkv"], file.Path);
        Assert.Equal(["x/y.mkv"], file.RawPath);
        Assert.Equal("x/y.mkv", read.RawName);
    }

    [Fact]
    public void Read_keeps_the_utf8_name_and_path_as_the_torrent_holds_them_before_the_plain_ones()
    {
        //Arrange
        // Transmission prefers them too: it drops a path read before path.utf-8, and the name read last wins.
        var content = TestTorrent.Bytes(10, seed: 4);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>
            {
                TestTorrent.Dictionary(
                    ("length", 10L), ("path", new List<object> { "Nie dobre.mkv" }), ("path.utf-8", new List<object> { "Сезон 1", "Добрий фільм.mkv" })),
            }),
            ("name", "Nie dobre"), ("name.utf-8", "Добрий фільм"), ("piece length", (long)Piece), ("pieces", SHA1.HashData(content))))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal("Добрий фільм", read!.RawName);
        Assert.Equal(["Сезон 1", "Добрий фільм.mkv"], Assert.Single(read.Files).RawPath);
    }

    [Fact]
    public void Read_cuts_each_name_as_the_torrent_holds_it_at_its_first_nul_where_libtorrent_drops_the_nul()
    {
        //Arrange
        var torrent = TestTorrent.V1("Show\0hidden", [(["E01.mkv\0.exe"], TestTorrent.Bytes(10, seed: 6))]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        var file = Assert.Single(read!.Files);
        Assert.Equal(["Showhidden", "E01.mkv.exe"], file.Path);
        Assert.Equal("Show", read.RawName);
        Assert.Equal(["E01.mkv"], file.RawPath);
        // Transmission 4.0 cuts an element only once cleaned, as the system reads the path: not the text above.
        Assert.False(file.RawPathIsExact);
    }

    [Fact]
    public void Read_marks_a_files_path_whose_bytes_are_not_utf8_as_not_exact_unless_path_utf8_names_it()
    {
        //Arrange
        // MAM-30 — an old torrent's Windows-1251 path ("Сезон 1", "Серія 01.mkv"): Transmission 4.0 writes those bytes
        // as they are, which no text names; path.utf-8 beside it is what every client reads instead.
        var content = TestTorrent.Bytes(10, seed: 22);
        byte[] season = [0xD1, 0xE5, 0xE7, 0xEE, 0xED, 0x20, 0x31];
        byte[] episode = [0xD1, 0xE5, 0xF0, 0xB3, 0xFF, 0x20, 0x30, 0x31, 0x2E, 0x6D, 0x6B, 0x76];
        byte[] TorrentOf(params (string Key, object Value)[] path) => TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object> { TestTorrent.Dictionary([("length", 10L), .. path.Select(entry => ((object)entry.Key, entry.Value))]) }),
            ("name", "Show"), ("piece length", (long)Piece), ("pieces", SHA1.HashData(content))))));
        var bytesOnly = TorrentOf(("path", new List<object> { season, episode }));
        var withUtf8 = TorrentOf(("path", new List<object> { season, episode }), ("path.utf-8", new List<object> { "Сезон 1", "Серія 01.mkv" }));

        //Act
        var readBytesOnly = TorrentContentReader.Read(bytesOnly);
        var readWithUtf8 = TorrentContentReader.Read(withUtf8);

        //Assert
        var file = Assert.Single(readBytesOnly!.Files);
        Assert.Equal(["����� 1", "���� 01.mkv"], file.RawPath);
        Assert.False(file.RawPathIsExact);
        var named = Assert.Single(readWithUtf8!.Files);
        Assert.Equal(["Сезон 1", "Серія 01.mkv"], named.RawPath);
        Assert.True(named.RawPathIsExact);
    }

    [Fact]
    public void Read_of_a_torrent_of_one_file_keeps_its_path_exact_whatever_the_bytes_of_its_name()
    {
        //Arrange
        // MAM-30 — a torrent's name is read as text by every client, Transmission 4.0 included: its U+FFFD are on disk.
        var content = TestTorrent.Bytes(10, seed: 23);
        // "Добрий.mkv" in Windows-1251.
        byte[] name = [0xC4, 0xEE, 0xE1, 0xF0, 0xE8, 0xE9, 0x2E, 0x6D, 0x6B, 0x76];
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 10L), ("name", name), ("piece length", (long)Piece), ("pieces", SHA1.HashData(content))))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        var file = Assert.Single(read!.Files);
        Assert.Equal(["������.mkv"], file.RawPath);
        Assert.True(file.RawPathIsExact);
    }

    [Fact]
    public void Read_of_a_v2_tree_marks_a_files_path_whose_keys_are_not_utf8_as_not_exact()
    {
        //Arrange
        var content = TestTorrent.Bytes(10, seed: 24);
        TorrentContent? ReadWithFolder(object folderKey) => TorrentContentReader.Read(TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(
                (folderKey, TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                    ("length", 10L), ("pieces root", PiecesRootOf(content)))))))))),
            ("meta version", 2L),
            ("name", "Show"),
            ("piece length", (long)Piece))))));

        //Act
        var notText = ReadWithFolder(new byte[] { 0xD1, 0xE5, 0xE7 });
        var text = ReadWithFolder("Сез");

        //Assert
        Assert.False(Assert.Single(notText!.Files).RawPathIsExact);
        Assert.True(Assert.Single(text!.Files).RawPathIsExact);
    }

    [Theory]
    // Valid sequences stay as they are.
    [InlineData(new byte[] { 0xE2, 0x82, 0xAC }, "€")]
    [InlineData(new byte[] { 0xF0, 0x9F, 0x8E, 0xAC }, "🎬")]
    // A byte that starts no sequence: a mark of its own.
    [InlineData(new byte[] { 0x41, 0x80, 0x42 }, "A\uFFFDB")]
    [InlineData(new byte[] { 0xF8, 0x88, 0x80, 0x80, 0x80 }, "\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD")]
    // Cut short by a byte that doesn't continue it: one mark for the lead byte and the continuation bytes after it.
    [InlineData(new byte[] { 0xE2, 0x82, 0x41 }, "\uFFFDA")]
    // Overlong, a surrogate, past U+10FFFF: one mark for the whole sequence, where .NET's decoder marks byte by byte.
    [InlineData(new byte[] { 0xC0, 0x80, 0x41 }, "\uFFFDA")]
    [InlineData(new byte[] { 0xE0, 0x80, 0x80 }, "\uFFFD")]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 }, "\uFFFD")]
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 }, "\uFFFD")]
    // Windows-1251 text, as old torrents hold it: "Аї" (C0 BF) and "а– 1" (E0 96 20 31).
    [InlineData(new byte[] { 0xC0, 0xBF }, "\uFFFD")]
    [InlineData(new byte[] { 0xE0, 0x96, 0x20, 0x31 }, "\uFFFD 1")]
    // A sequence the text ends inside: one mark, the last.
    [InlineData(new byte[] { 0x41, 0xE2, 0x82 }, "A\uFFFD")]
    // Cut at the first NUL first, even inside a sequence.
    [InlineData(new byte[] { 0x41, 0xE2, 0x00, 0x82, 0x42 }, "A\uFFFD")]
    public void Read_reads_each_name_as_the_torrent_holds_it_as_transmission_reads_text(byte[] name, string expected)
    {
        //Arrange
        // MAM-30 — utf8cpp's replace_invalid, which Transmission writes names with: the U+FFFD on disk are its.
        var content = TestTorrent.Bytes(10, seed: 21);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object> { TestTorrent.Dictionary(("length", 10L), ("path", new List<object> { name })) }),
            ("name", name), ("piece length", (long)Piece), ("pieces", SHA1.HashData(content))))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal(expected, read!.RawName);
        var file = Assert.Single(read.Files);
        Assert.Equal([expected], file.RawPath);
        // Exact only when reading changed nothing: every case here that isn't valid UTF-8 reads with a U+FFFD.
        Assert.Equal(!expected.Contains('�', StringComparison.Ordinal), file.RawPathIsExact);
    }

    [Fact]
    public void Read_of_a_torrent_of_several_files_with_an_empty_name_puts_them_in_a_folder_named_underscore()
    {
        //Arrange
        var torrent = TestTorrent.V1("", [(["a.mkv"], TestTorrent.Bytes(10, seed: 6))]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal(["_", "a.mkv"], Assert.Single(read!.Files).Path);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("/")]
    [InlineData("‮")]
    public void Read_of_a_torrent_of_several_files_whose_name_cleaning_drops_names_their_folder_by_its_v1_info_hash(string name)
    {
        //Arrange
        var torrent = TestTorrent.V1(name, [(["a.mkv"], TestTorrent.Bytes(10, seed: 6))]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal([TorrentInfoHashReader.FromTorrentFile(torrent).V1!, "a.mkv"], Assert.Single(read!.Files).Path);
    }

    [Fact]
    public void Read_of_a_v2_torrent_whose_name_cleaning_drops_names_its_folder_by_the_sha1_of_its_info_dictionary()
    {
        //Arrange
        var first = TestTorrent.Bytes(10, seed: 6);
        var second = TestTorrent.Bytes(20, seed: 7);
        var info = TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(
                ("a.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(("length", 10L), ("pieces root", PiecesRootOf(first)))))),
                ("b.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(("length", 20L), ("pieces root", PiecesRootOf(second)))))))),
            ("meta version", 2L),
            ("name", ".."),
            ("piece length", (long)Piece));
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", info)));
        var folder = Convert.ToHexStringLower(SHA1.HashData(TestTorrent.Encode(info)));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        // libtorrent names the folder before it clears a v2 torrent's v1 hash: 40 characters, never the SHA-256's 64.
        Assert.Equal([[folder, "a.mkv"], [folder, "b.mkv"]], read!.Files.Select(file => file.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("/")]
    public void Read_of_a_torrent_of_one_file_whose_name_is_empty_or_dropped_is_no_content_as_libtorrent_refuses_it(string name)
    {
        //Arrange
        var torrent = TestTorrent.V1(name, TestTorrent.Bytes(10, seed: 6));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Null(read);
    }

    [Fact]
    public void Read_of_a_v2_tree_makes_a_dot_folder_an_underscore_as_libtorrent_forces_it()
    {
        //Arrange
        var content = TestTorrent.Bytes(10, seed: 12);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(
                ("..", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                    ("length", 10L), ("pieces root", PiecesRootOf(content)))))))))),
            ("meta version", 2L),
            ("name", "Show"),
            ("piece length", (long)Piece)))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Equal(["Show", "_", "x.mkv"], Assert.Single(read!.Files).Path);
    }

    [Fact]
    public void Read_of_a_v2_tree_keeps_each_key_below_the_torrents_folder_as_the_tree_holds_it()
    {
        //Arrange
        // MAM-30 — for a torrent of one file, its key is its name, whatever the torrent's name says.
        var content = TestTorrent.Bytes(10, seed: 12);
        var several = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(
                ("..", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                    ("length", 10L), ("pieces root", PiecesRootOf(content)))))))))),
            ("meta version", 2L),
            ("name", "Show "),
            ("piece length", (long)Piece)))));
        var one = TestTorrent.V2("Another name", [(["Film.mkv"], content)]);

        //Act
        var readSeveral = TorrentContentReader.Read(several);
        var readOne = TorrentContentReader.Read(one);

        //Assert
        Assert.Equal("Show ", readSeveral!.RawName);
        Assert.Equal(["..", "x.mkv"], Assert.Single(readSeveral.Files).RawPath);
        Assert.False(readOne!.HasRootFolder);
        Assert.Equal("Another name", readOne.RawName);
        Assert.Equal(["Film.mkv"], Assert.Single(readOne.Files).RawPath);
    }

    [Fact]
    public void Read_of_a_hybrid_torrent_compares_its_v1_pieces()
    {
        //Arrange
        var content = TestTorrent.Bytes(Piece, seed: 7);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                ("length", (long)Piece), ("pieces root", new byte[32]))))))),
            ("length", (long)Piece),
            ("meta version", 2L),
            ("name", "x.mkv"),
            ("piece length", (long)Piece),
            ("pieces", SHA1.HashData(content))))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        var sample = Assert.Single(Assert.Single(read!.Files).Samples);
        Assert.Equal(FilePieceHashing.Sha1, sample.Range.Hashing);
        Assert.Equal(SHA1.HashData(content), sample.ExpectedHash);
    }

    [Fact]
    public void Read_of_a_hybrid_torrent_keeps_its_v1_name_and_files_as_the_torrent_holds_them_never_its_file_tree_keys()
    {
        //Arrange
        // MAM-30 — Transmission ignores the file tree ("'file tree' is ignored", 4.0.6 and 4.1.3) and writes the v1
        // name and files, here named otherwise than the tree's keys.
        var content = TestTorrent.Bytes(Piece, seed: 7);
        var root = PiecesRootOf(content);
        var one = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                ("length", (long)Piece), ("pieces root", root))))))),
            ("length", (long)Piece),
            ("meta version", 2L),
            ("name", "x: y.mkv"),
            ("piece length", (long)Piece),
            ("pieces", SHA1.HashData(content))))));
        var several = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("E01.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                ("length", (long)Piece), ("pieces root", root))))))),
            ("files", new List<object> { TestTorrent.Dictionary(("length", (long)Piece), ("path", new List<object> { "S01", "E01: x.mkv" })) }),
            ("meta version", 2L),
            ("name", "Show: 1"),
            ("piece length", (long)Piece),
            ("pieces", SHA1.HashData(content))))));

        //Act
        var readOne = TorrentContentReader.Read(one);
        var readSeveral = TorrentContentReader.Read(several);

        //Assert
        Assert.Equal("x: y.mkv", readOne!.RawName);
        Assert.Equal(["x: y.mkv"], Assert.Single(readOne.Files).RawPath);
        Assert.Equal("Show: 1", readSeveral!.RawName);
        Assert.Equal(["S01", "E01: x.mkv"], Assert.Single(readSeveral.Files).RawPath);
    }

    [Fact]
    public void Read_of_a_v2_torrent_of_one_file_compares_the_file_as_one_tree_to_its_pieces_root()
    {
        //Arrange
        var content = TestTorrent.Bytes((3 * Piece) + 5, seed: 8);
        var torrent = TestTorrent.V2("Film.mkv", [(["Film.mkv"], content)], pieceLength: 4 * Piece);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.NotNull(read);
        Assert.False(read.HasRootFolder);
        var file = Assert.Single(read.Files);
        Assert.Equal(["Film.mkv"], file.Path);
        var sample = Assert.Single(file.Samples);
        Assert.Equal(new FilePieceRange(0, content.Length, FilePieceHashing.Sha256Tree, 4), sample.Range);
        Assert.Equal(TorrentPieceHash.Of(content, FilePieceHashing.Sha256Tree, 4), sample.ExpectedHash);
    }

    [Fact]
    public void Read_of_a_v2_torrent_of_several_files_compares_pieces_from_its_layers()
    {
        //Arrange
        var episode = TestTorrent.Bytes((5 * Piece) + 3, seed: 9);
        var subtitles = TestTorrent.Bytes(50, seed: 10);
        var torrent = TestTorrent.V2("Pack", [(["S01", "E01.mkv"], episode), (["S01", "E01.srt"], subtitles)]);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.NotNull(read);
        Assert.True(read.HasRootFolder);
        var file = read.Files.Single(candidate => candidate.Path[^1] == "E01.mkv");
        Assert.Equal(["Pack", "S01", "E01.mkv"], file.Path);
        Assert.Equal([(0L, Piece), (2L * Piece, Piece), (5L * Piece, 3)], file.Samples.Select(sample => (sample.Range.Offset, sample.Range.Length)));
        Assert.All(file.Samples, sample => Assert.Equal(
            TorrentPieceHash.Of(episode.AsSpan((int)sample.Range.Offset, sample.Range.Length), FilePieceHashing.Sha256Tree, 1),
            sample.ExpectedHash));
    }

    [Fact]
    public void Read_of_a_v2_file_of_two_blocks_in_a_longer_piece_compares_it_as_a_tree_of_two_leaves()
    {
        //Arrange
        var content = TestTorrent.Bytes(2 * Piece, seed: 13);
        var torrent = TestTorrent.V2("Film.mkv", [(["Film.mkv"], content)], pieceLength: 4 * Piece);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        // The file's pieces root is the tree over its own blocks, padded to a power of two: 2 leaves here, not the 4
        // of a whole piece, which is the width that makes the same bytes hash the same.
        var sample = Assert.Single(Assert.Single(read!.Files).Samples);
        Assert.Equal(new FilePieceRange(0, content.Length, FilePieceHashing.Sha256Tree, 2), sample.Range);
        Assert.Equal(TorrentPieceHash.Of(content, FilePieceHashing.Sha256Tree, 2), sample.ExpectedHash);
        Assert.NotEqual(TorrentPieceHash.Of(content, FilePieceHashing.Sha256Tree, 4), sample.ExpectedHash);
    }

    [Fact]
    public void Read_of_a_v2_file_of_two_and_a_half_pieces_compares_each_piece_as_a_tree_as_wide_as_a_whole_piece()
    {
        //Arrange
        var pieceLength = 4 * Piece;
        var content = TestTorrent.Bytes((2 * pieceLength) + (2 * Piece), seed: 14);
        var torrent = TestTorrent.V2("Pack", [(["E01.mkv"], content), (["E01.srt"], TestTorrent.Bytes(10, seed: 15))], pieceLength: pieceLength);

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        // A piece layer pads its last piece to a whole piece's leaves (BEP 52): the half piece is a tree of 4 too.
        var file = read!.Files.Single(candidate => candidate.Path[^1] == "E01.mkv");
        Assert.Equal(
            [(0L, pieceLength, 4), ((long)pieceLength, pieceLength, 4), (2L * pieceLength, 2 * Piece, 4)],
            file.Samples.Select(sample => (sample.Range.Offset, sample.Range.Length, sample.Range.TreeLeafCount)));
        Assert.All(file.Samples, sample => Assert.Equal(
            TorrentPieceHash.Of(content.AsSpan((int)sample.Range.Offset, sample.Range.Length), FilePieceHashing.Sha256Tree, 4),
            sample.ExpectedHash));
    }

    [Fact]
    public void Read_of_a_v2_file_longer_than_a_piece_without_its_layer_is_told_by_its_size_alone()
    {
        //Arrange
        var content = TestTorrent.Bytes((2 * Piece) + 1, seed: 11);
        var root = TorrentPieceHash.Of(content.AsSpan(0, content.Length), FilePieceHashing.Sha256Tree, 4);
        var torrent = TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(
                ("length", (long)content.Length), ("pieces root", root))))))),
            ("meta version", 2L),
            ("name", "x.mkv"),
            ("piece length", (long)Piece)))));

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Empty(Assert.Single(read!.Files).Samples);
    }

    public static TheoryData<string, byte[]> MalformedTorrents => new()
    {
        { "not bencode", "this is no torrent"u8.ToArray() },
        { "no info dictionary", "d8:announce19:http://tracker.test3:fooi1ee"u8.ToArray() },
        { "a piece length of 0", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L), ("name", "x.mkv"), ("piece length", 0L), ("pieces", new byte[20]))))) },
        { "pieces for two pieces of one byte", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L), ("name", "x.mkv"), ("piece length", (long)Piece), ("pieces", new byte[40]))))) },
        { "pieces not a multiple of 20 bytes", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L), ("name", "x.mkv"), ("piece length", (long)Piece), ("pieces", new byte[19]))))) },
        { "an empty list of files", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object>()), ("name", "Show"), ("piece length", (long)Piece), ("pieces", new byte[0]))))) },
        { "a file without a length", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object> { TestTorrent.Dictionary(("path", new List<object> { "a.mkv" })) }),
            ("name", "Show"), ("piece length", (long)Piece), ("pieces", new byte[0]))))) },
        { "a v2 piece length that isn't a power of two", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(("length", 1L), ("pieces root", new byte[32]))))))),
            ("meta version", 2L), ("name", "x.mkv"), ("piece length", 24_576L))))) },
        { "a v2 file without its pieces root", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(("length", 1L))))))),
            ("meta version", 2L), ("name", "x.mkv"), ("piece length", (long)Piece))))) },
        // libtorrent's largest piece is 32767 blocks: one byte more is a torrent it refuses.
        { "a piece length libtorrent refuses", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L), ("name", "x.mkv"), ("piece length", (32_767L * Piece) + 1), ("pieces", new byte[20]))))) },
        { "no name", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("length", 1L), ("piece length", (long)Piece), ("pieces", new byte[20]))))) },
        { "a file with an empty path", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("files", new List<object> { TestTorrent.Dictionary(("length", 1L), ("path", new List<object>())) }),
            ("name", "Show"), ("piece length", (long)Piece), ("pieces", new byte[20]))))) },
        { "a v2 folder with an empty name", TestTorrent.Encode(TestTorrent.Dictionary(("info", TestTorrent.Dictionary(
            ("file tree", TestTorrent.Dictionary(("", TestTorrent.Dictionary(("x.mkv", TestTorrent.Dictionary(("", TestTorrent.Dictionary(("length", 0L))))))))),
            ("meta version", 2L), ("name", "Show"), ("piece length", (long)Piece))))) },
    };

    [Theory]
    [MemberData(nameof(MalformedTorrents))]
    public void Read_of_a_malformed_torrent_is_no_content(string label, byte[] torrent)
    {
        //Arrange
        _ = label;

        //Act
        var read = TorrentContentReader.Read(torrent);

        //Assert
        Assert.Null(read);
    }

    private static byte[] Sha1Of(byte[] content, FilePieceRange range) => SHA1.HashData(content.AsSpan((int)range.Offset, range.Length));

    /// <summary>A v2 file's pieces root as BEP 52 builds it: the tree over its blocks, padded to a power of two.</summary>
    private static byte[] PiecesRootOf(byte[] content)
    {
        var blocks = Math.Max(1, (content.Length + TorrentPieceHash.BlockSize - 1) / TorrentPieceHash.BlockSize);
        var width = 1;
        while (width < blocks)
            width *= 2;
        return TorrentPieceHash.Of(content, FilePieceHashing.Sha256Tree, width);
    }
}
