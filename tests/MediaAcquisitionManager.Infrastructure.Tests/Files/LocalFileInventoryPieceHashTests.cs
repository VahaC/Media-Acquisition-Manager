using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-38 — a few pieces of a real file in a temp folder hashed the way a torrent hashes them (ADR 0034): SHA-1 or a
// v2 tree, each read from where it starts a chunk at a time, so a piece of libtorrent's largest size never sits in
// memory whole, with the file's facts; every refusal of Describe before a byte is read (ADR 0009); a piece past the
// file's end is a file that changed while it was read, never a hash. The overwrite check that calls this, in
// Api.Tests.
public sealed class LocalFileInventoryPieceHashTests : IDisposable
{
    private readonly TemporaryFolder _folder = new("mam-file-pieces-");
    private readonly LocalFileInventory _inventory = new(new FfprobeProcess());
    private readonly string _media;
    private readonly string _outside;

    public LocalFileInventoryPieceHashTests()
    {
        _media = _folder.CreateFolder("media");
        _outside = _folder.CreateFolder("outside");
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task HashPiecesAsync_hashes_each_piece_where_it_lies_with_the_files_facts()
    {
        //Arrange
        var content = TestTorrent.Bytes((3 * TestTorrent.PieceLength) + 7, seed: 1);
        var path = Plant("film.mkv", content);
        FilePieceRange[] pieces =
        [
            new(0, TestTorrent.PieceLength, FilePieceHashing.Sha1, 0),
            new(3 * TestTorrent.PieceLength, 7, FilePieceHashing.Sha1, 0),
            new(TestTorrent.PieceLength, 2 * TestTorrent.PieceLength, FilePieceHashing.Sha256Tree, 2),
        ];

        //Act
        var result = await _inventory.HashPiecesAsync(path, [_media], pieces, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(content.Length, result.Value!.Facts.SizeBytes);
        Assert.Equal(
            [
                SHA1.HashData(content.AsSpan(0, TestTorrent.PieceLength)),
                SHA1.HashData(content.AsSpan(3 * TestTorrent.PieceLength)),
                TorrentPieceHash.Of(content.AsSpan(TestTorrent.PieceLength, 2 * TestTorrent.PieceLength), FilePieceHashing.Sha256Tree, 2),
            ],
            result.Value.Hashes);
    }

    [Fact]
    public async Task HashPiecesAsync_hashes_a_piece_longer_than_one_read_a_chunk_at_a_time_as_one_read_would()
    {
        //Arrange
        // A chunk of one 16 KiB block: a piece of five blocks and a bit is read and hashed in six chunks.
        var inventory = new LocalFileInventory(new FfprobeProcess()) { HashChunkSize = 1 };
        var length = (5 * TestTorrent.PieceLength) + 9;
        var content = TestTorrent.Bytes(length + 3, seed: 4);
        var path = Plant("long.mkv", content);
        FilePieceRange[] pieces =
        [
            new(3, length, FilePieceHashing.Sha1, 0),
            new(3, length, FilePieceHashing.Sha256Tree, 8),
        ];

        //Act
        var result = await inventory.HashPiecesAsync(path, [_media], pieces, TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.Found, result.Status);
        Assert.Equal(
            [
                SHA1.HashData(content.AsSpan(3, length)),
                TorrentPieceHash.Of(content.AsSpan(3, length), FilePieceHashing.Sha256Tree, 8),
            ],
            result.Value!.Hashes);
    }

    [Fact]
    public async Task HashPiecesAsync_of_a_piece_past_the_files_end_changed_while_reading()
    {
        //Arrange
        var path = Plant("short.mkv", TestTorrent.Bytes(100, seed: 2));

        //Act
        var result = await _inventory.HashPiecesAsync(
            path, [_media], [new FilePieceRange(50, 100, FilePieceHashing.Sha1, 0)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.ChangedWhileReading, result.Status);
        Assert.Null(result.Value);
        Assert.Equal($"“{path}” changed while the app was reading some of its pieces, so they can't be said to belong to either version.", result.Error);
    }

    [Fact]
    public async Task HashPiecesAsync_outside_the_allowed_roots_reads_nothing()
    {
        //Arrange
        var path = Path.Combine(_outside, "film.mkv");
        File.WriteAllBytes(path, TestTorrent.Bytes(10, seed: 3));

        //Act
        var result = await _inventory.HashPiecesAsync(
            path, [_media], [new FilePieceRange(0, 10, FilePieceHashing.Sha1, 0)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.OutsideAllowedRoots, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task HashPiecesAsync_of_nothing_at_the_path_is_not_found()
    {
        //Arrange
        var path = Path.Combine(_media, "missing.mkv");

        //Act
        var result = await _inventory.HashPiecesAsync(
            path, [_media], [new FilePieceRange(0, 10, FilePieceHashing.Sha1, 0)], TestContext.Current.CancellationToken);

        //Assert
        Assert.Equal(FileInventoryStatus.NotFound, result.Status);
    }

    private string Plant(string name, byte[] content)
    {
        var path = Path.Combine(_media, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}
