using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-38 — a piece's hash the way the torrent's author worked it out: SHA-1 for v1; for v2 the root of a SHA-256
// tree over 16 KiB blocks, zero leaves after the last block up to the tree's width, the nodes above them hashed
// like any other (BEP 52, as libtorrent pads). Pinned against SHA-256 computed by hand, node by node.
public sealed class TorrentPieceHashTests
{
    private const int Block = TorrentPieceHash.BlockSize;

    [Fact]
    public void Of_a_v1_piece_is_the_sha1_of_its_bytes()
    {
        //Arrange
        var piece = "The quick brown fox jumps over the lazy dog"u8.ToArray();

        //Act
        var hash = TorrentPieceHash.Of(piece, FilePieceHashing.Sha1, 0);

        //Assert
        Assert.Equal("2fd4e1c67a2d28fced849ee1bb76e7391b93eb12", Convert.ToHexStringLower(hash));
    }

    [Fact]
    public void Of_a_tree_of_one_block_is_the_sha256_of_the_block()
    {
        //Arrange
        var piece = new byte[100];
        piece.AsSpan().Fill(7);

        //Act
        var hash = TorrentPieceHash.Of(piece, FilePieceHashing.Sha256Tree, 1);

        //Assert
        Assert.Equal(SHA256.HashData(piece), hash);
    }

    [Fact]
    public void Of_a_tree_pads_with_zero_leaves_and_hashes_the_nodes_above_them()
    {
        //Arrange
        var piece = new byte[Block + 10];
        piece.AsSpan(0, Block).Fill(1);
        piece.AsSpan(Block).Fill(2);
        var first = SHA256.HashData(piece.AsSpan(0, Block));
        var second = SHA256.HashData(piece.AsSpan(Block));
        var zero = new byte[32];
        byte[] expected = SHA256.HashData([.. SHA256.HashData([.. first, .. second]), .. SHA256.HashData([.. zero, .. zero])]);

        //Act
        var hash = TorrentPieceHash.Of(piece, FilePieceHashing.Sha256Tree, 4);

        //Assert
        Assert.Equal(expected, hash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0)]
    public void Of_a_tree_whose_width_is_too_small_or_not_a_power_of_two_throws(int leafCount)
    {
        //Arrange
        var piece = new byte[Block + 1];

        //Act
        var thrown = Record.Exception(() => TorrentPieceHash.Of(piece, FilePieceHashing.Sha256Tree, leafCount));

        //Assert
        Assert.IsType<ArgumentOutOfRangeException>(thrown);
    }
}
