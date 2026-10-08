namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-38 — one run of a file's bytes to hash: a torrent's piece that lies wholly inside the file.
/// </summary>
/// <param name="Offset">Where the piece starts in the file.</param>
/// <param name="Length">How many bytes it has.</param>
/// <param name="Hashing">How it is hashed.</param>
/// <param name="TreeLeafCount">For <see cref="FilePieceHashing.Sha256Tree"/>, how many 16 KiB leaves the tree
/// has, a power of two: the blocks the piece holds, and zero leaves after them. 0 for
/// <see cref="FilePieceHashing.Sha1"/>.</param>
public sealed record FilePieceRange(long Offset, int Length, FilePieceHashing Hashing, int TreeLeafCount);
