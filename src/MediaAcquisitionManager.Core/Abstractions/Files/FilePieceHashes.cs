namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// MAM-38 — the hashes of some pieces of one file, with the facts of the version they were read from.
/// </summary>
/// <param name="Facts">The file's facts, read before and after the pieces and the same both times.</param>
/// <param name="Hashes">One hash per piece asked for, in the order asked.</param>
public sealed record FilePieceHashes(FileFacts Facts, IReadOnlyList<byte[]> Hashes);
