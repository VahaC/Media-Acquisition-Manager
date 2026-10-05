namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>What one full read of a file found (MAM-19).</summary>
/// <param name="File">What the file system said about the file, the same before and after the read: the
/// key the hash is kept under.</param>
/// <param name="Sha256">The SHA-256 of every byte of the file, in lowercase hex — what <c>sha256sum</c>
/// prints for it.</param>
public sealed record FileContentHash(FileFacts File, string Sha256);
