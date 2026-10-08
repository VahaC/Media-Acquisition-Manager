namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-38 — how a piece of a file is hashed to compare it with a torrent's own hash of that piece. Stored
/// nowhere.
/// </summary>
public enum FilePieceHashing
{
    /// <summary>SHA-1 of the piece's bytes: a v1 torrent's <c>pieces</c> (BEP 3), a hybrid's too.</summary>
    Sha1 = 1,

    /// <summary>
    /// The root of a SHA-256 Merkle tree over the piece's 16 KiB blocks, padded with zero leaves to
    /// <see cref="FilePieceRange.TreeLeafCount"/>: a v2 torrent's piece layers and pieces roots (BEP 52).
    /// </summary>
    Sha256Tree = 2,
}
