using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — how one download client's whole listing was read for the held marks (ADR 0031); anything but
/// <see cref="ReplacementClientCheck.Read"/> is uncertain, never an all-clear.</summary>
/// <param name="ClientName">The client.</param>
/// <param name="Status">Read, read in part, or not read.</param>
/// <param name="Error">Why it wasn't read, every secret of the call masked; <c>null</c> unless
/// <see cref="ReplacementClientCheck.NotRead"/>.</param>
/// <param name="UnplacedTorrents">How many of its torrents' files couldn't be placed in the app's folders.</param>
public sealed record ReplacementClientReading(string ClientName, ReplacementClientCheck Status, string? Error, int UnplacedTorrents);
