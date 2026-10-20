namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a size in a sentence. The only sizes in server sentences are limits (8 MiB, 4 MiB), so the
/// renderer names the exact unit (<c>8 MiB</c>, <c>1536 KiB</c>, <c>1000 bytes</c>), as today's sentences do, never a
/// rounded one.
/// </summary>
/// <param name="Bytes">The size in bytes.</param>
public sealed record SizeValue(long Bytes) : MessageValue;
