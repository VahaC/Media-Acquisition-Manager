using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>A release's payload, fetched by the server when the release is submitted.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="TorrentFile">The .torrent file's bytes, for <see cref="ReleasePayloadKind.TorrentFile"/>.</param>
/// <param name="MagnetUri">The magnet link, for <see cref="ReleasePayloadKind.Magnet"/>.</param>
public sealed record ReleasePayload(ReleasePayloadKind Kind, byte[]? TorrentFile, string? MagnetUri)
{
    /// <summary>The kind only.</summary>
    /// <remarks>MAM-7 — a magnet can carry tracker passkeys, and a record's generated
    /// <c>ToString</c> would log it.</remarks>
    public override string ToString() => $"ReleasePayload {{ Kind = {Kind} }}";
}
