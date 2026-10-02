namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// The info hashes that name one torrent, as the app worked them out from its payload (MAM-13):
/// BitTorrent v1's SHA-1 and v2's SHA-256 of the torrent's info dictionary, in lower-case hexadecimal. A
/// hybrid torrent has both, a v1 or v2 torrent one, and a payload the app couldn't read neither.
/// </summary>
/// <param name="V1">40 hexadecimal characters; <c>null</c> when the torrent has no v1 metadata or the
/// payload doesn't say.</param>
/// <param name="V2">64 hexadecimal characters; <c>null</c> when the torrent has no v2 metadata or the
/// payload doesn't say.</param>
public sealed record TorrentInfoHashes(string? V1, string? V2)
{
    /// <summary>Neither hash: the payload names no torrent the app can tell.</summary>
    public static TorrentInfoHashes None { get; } = new(null, null);

    /// <summary>Whether either hash is known.</summary>
    public bool IsKnown => V1 is not null || V2 is not null;

    /// <summary>
    /// The one hash to name the torrent by where a single one is asked for: v1 when there is one, otherwise
    /// v2. Not necessarily a client's own id for it — qBittorrent built on libtorrent 2 names a hybrid torrent
    /// by its v2 hash cut to 40 characters — so a client's job list is searched by <see cref="All"/>.
    /// </summary>
    public string? Primary => V1 ?? V2;

    /// <summary>Every known hash, v1 first.</summary>
    public IReadOnlyList<string> All => (V1, V2) switch
    {
        ({ } v1, { } v2) => [v1, v2],
        ({ } v1, null) => [v1],
        (null, { } v2) => [v2],
        _ => [],
    };

    /// <summary>Whether <paramref name="hash"/> is one of these, in any letter case.</summary>
    public bool Contains(string? hash) =>
        hash is not null
        && (string.Equals(hash, V1, StringComparison.OrdinalIgnoreCase) || string.Equals(hash, V2, StringComparison.OrdinalIgnoreCase));
}
