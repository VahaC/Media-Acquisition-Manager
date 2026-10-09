using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-24 — how one download client's torrents were read for the held marks (ADR 0031).</summary>
/// <param name="ClientName">The client.</param>
/// <param name="Status">Read, read in part, or not read; anything but read is uncertain, never an all-clear.</param>
/// <param name="Error">Why it wasn't read, every secret masked; <c>null</c> unless not read.</param>
/// <param name="UnplacedTorrents">How many of its torrents' files couldn't be placed in the app's folders.</param>
public sealed record ReplacementClientCheckDto(string ClientName, ReplacementClientCheck Status, string? Error, int UnplacedTorrents);
