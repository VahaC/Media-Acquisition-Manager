using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-26 — one name <see cref="IReplacementHoldingReader"/> asks which torrents hold (ADR 0031): an old copy the Download
/// dialog may tick, or a video or a companion the move would rename. Read by path, confirmed by identity, so it carries
/// both.
/// </summary>
/// <param name="AppPath">Where the name is inside the app.</param>
/// <param name="Fresh">What the disk said of it during this read: its size picks the torrent files to look at, its device
/// and inode confirm one, and its link count tells a second name.</param>
public sealed record ReplacementHoldingTarget(NormalizedPath AppPath, FileFacts Fresh);
