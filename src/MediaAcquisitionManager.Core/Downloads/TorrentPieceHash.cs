using System.Numerics;
using System.Security.Cryptography;
using MediaAcquisitionManager.Core.Abstractions.Files;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// MAM-38 — a torrent piece's hash worked out from its bytes, the way the torrent's author worked it out: SHA-1
/// for v1 (BEP 3), the root of a SHA-256 Merkle tree over 16 KiB blocks for v2 (BEP 52).
/// </summary>
public static class TorrentPieceHash
{
    /// <summary>A v2 tree's leaf: 16 KiB of the file (BEP 52).</summary>
    public const int BlockSize = 16 * 1024;

    private const int Sha256Length = 32;

    /// <summary>The hash of <paramref name="piece"/>.</summary>
    /// <param name="piece">The piece's bytes.</param>
    /// <param name="hashing">How the torrent hashed it.</param>
    /// <param name="treeLeafCount">For <see cref="FilePieceHashing.Sha256Tree"/>, the tree's leaves, a power of
    /// two no smaller than the piece's blocks.</param>
    public static byte[] Of(ReadOnlySpan<byte> piece, FilePieceHashing hashing, int treeLeafCount) => hashing switch
    {
        FilePieceHashing.Sha1 => SHA1.HashData(piece),
        FilePieceHashing.Sha256Tree => TreeRoot(piece, treeLeafCount),
        _ => throw new ArgumentOutOfRangeException(nameof(hashing), hashing, "Not a way to hash a piece."),
    };

    /// <summary>
    /// The root of a tree over <paramref name="leaves"/>: <paramref name="leafCount"/> SHA-256 hashes one after
    /// another — each block's, then zero leaves up to the count. What a piece read block by block gives, so a long
    /// piece is never held whole in memory (<c>LocalFileInventory</c>). The levels are hashed in place, over the
    /// leaves given.
    /// </summary>
    public static byte[] RootOf(Span<byte> leaves, int leafCount)
    {
        if (leafCount < 1 || !BitOperations.IsPow2(leafCount) || leaves.Length != leafCount * Sha256Length)
            throw new ArgumentOutOfRangeException(nameof(leafCount), leafCount, "A tree's leaves are a power of two, each a SHA-256.");

        // Each level in place, pair by pair, through a buffer: a pair's hash would otherwise overwrite the pair it
        // is still reading.
        Span<byte> parent = stackalloc byte[Sha256Length];
        for (var count = leafCount; count > 1; count /= 2)
        {
            for (var pair = 0; pair < count / 2; pair++)
            {
                SHA256.HashData(leaves.Slice(pair * 2 * Sha256Length, 2 * Sha256Length), parent);
                parent.CopyTo(leaves.Slice(pair * Sha256Length, Sha256Length));
            }
        }

        return leaves[..Sha256Length].ToArray();
    }

    /// <summary>
    /// The root over the piece's blocks — each its SHA-256, the last one as short as it is — and zero leaves after
    /// them up to <paramref name="leafCount"/>; the nodes above the zero leaves are hashed like any other, as
    /// libtorrent pads a tree.
    /// </summary>
    private static byte[] TreeRoot(ReadOnlySpan<byte> piece, int leafCount)
    {
        var blocks = (piece.Length + BlockSize - 1) / BlockSize;
        if (leafCount < Math.Max(blocks, 1) || !BitOperations.IsPow2(leafCount))
            throw new ArgumentOutOfRangeException(nameof(leafCount), leafCount, "A tree's leaves are a power of two no smaller than its blocks.");

        // Zero bytes are the zero leaves past the piece's last block.
        var layer = new byte[leafCount * Sha256Length];
        for (var block = 0; block < blocks; block++)
        {
            var start = block * BlockSize;
            SHA256.HashData(piece.Slice(start, Math.Min(BlockSize, piece.Length - start)), layer.AsSpan(block * Sha256Length, Sha256Length));
        }

        return RootOf(layer, leafCount);
    }
}
