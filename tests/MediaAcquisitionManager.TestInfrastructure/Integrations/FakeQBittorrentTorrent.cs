using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// One torrent a <see cref="FakeQBittorrentServer"/> has (MAM-13): what <c>torrents/info</c> lists for it, in
/// qBittorrent's own terms — and, MAM-14, what <c>torrents/files</c> lists (<see cref="Files"/>); MAM-24, whether
/// <c>torrents/info?includeFiles=true</c> lists those files with it (<see cref="HasMetadata"/>).
/// </summary>
/// <param name="Hash">Its TorrentID, lower-case (<see cref="IdOf"/>).</param>
/// <param name="InfoHashV1">The v1 info hash; <c>""</c> when it has none, as qBittorrent lists it.</param>
/// <param name="InfoHashV2">The v2 info hash; <c>""</c> when it has none.</param>
/// <param name="Name">Its name.</param>
/// <param name="Category">Its category; <c>""</c> for none.</param>
/// <param name="State">qBittorrent's state, such as <c>stalledUP</c> or <c>downloading</c>.</param>
/// <param name="Progress">From 0 to 1.</param>
/// <param name="SavePath">The folder it saves to.</param>
/// <param name="ContentPath">Where its content is.</param>
/// <param name="AutoTmm">Whether automatic torrent management places it.</param>
public sealed record FakeQBittorrentTorrent(
    string Hash,
    string InfoHashV1,
    string InfoHashV2,
    string Name,
    string Category,
    string State,
    double Progress,
    string SavePath,
    string ContentPath,
    bool AutoTmm)
{
    /// <summary>
    /// MAM-14 — its files, in qBittorrent's order; none unless a test gives some (<c>with { Files = … }</c>),
    /// as for a magnet still fetching its metadata.
    /// </summary>
    public IReadOnlyList<FakeQBittorrentFile> Files { get; init; } = [];

    /// <summary>
    /// MAM-24 — whether qBittorrent has the torrent's metadata, listed as <c>has_metadata</c>: 5.2 lists <c>files</c>
    /// with a torrent in <c>torrents/info?includeFiles=true</c> only when it has, so <c>false</c> is a magnet still
    /// fetching it — no <c>files</c> at all, which <see cref="Files"/> empty can't tell from a list of none. True
    /// unless a test says otherwise (<c>with { HasMetadata = false }</c>), or the torrent came from a magnet sent
    /// through <c>torrents/add</c>.
    /// </summary>
    public bool HasMetadata { get; init; } = true;

    /// <summary>
    /// A torrent named by <paramref name="hashes"/>, which must name at least one, and saved to
    /// <paramref name="savePath"/> — finished and seeding unless a test says otherwise.
    /// </summary>
    public static FakeQBittorrentTorrent Of(
        TorrentInfoHashes hashes,
        string name,
        string category = "",
        string state = "stalledUP",
        double progress = 1,
        string savePath = FakeQBittorrentServer.SampleDefaultSavePath,
        bool autoTmm = true)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (!hashes.IsKnown)
            throw new ArgumentException("A torrent qBittorrent has is named by at least one info hash.", nameof(hashes));

        return new FakeQBittorrentTorrent(
            IdOf(hashes), hashes.V1 ?? "", hashes.V2 ?? "", name, category, state, progress, savePath, $"{savePath}/{name}", autoTmm);
    }

    /// <summary>
    /// The TorrentID qBittorrent names a torrent of <paramref name="hashes"/> by: the v2 info hash cut to 40
    /// characters when there is one, otherwise the v1 hash. A hybrid torrent is named by its v2 hash, as
    /// libtorrent 2's <c>info_hash_t::get_best</c> names it in the builds 5.2.3 ships (a libtorrent 1.2 build
    /// knows only v1) — so a lookup by a hybrid's v1 hash alone doesn't find it.
    /// </summary>
    public static string IdOf(TorrentInfoHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        return hashes.V2?[..40] ?? hashes.V1 ?? throw new ArgumentException("No info hash to name the torrent by.", nameof(hashes));
    }

    /// <summary>Whether this torrent is the one <paramref name="hashes"/> name, by its id or either hash.</summary>
    public bool IsNamedBy(TorrentInfoHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        return (hashes.IsKnown && string.Equals(Hash, IdOf(hashes), StringComparison.OrdinalIgnoreCase))
            || (InfoHashV1.Length > 0 && hashes.Contains(InfoHashV1))
            || (InfoHashV2.Length > 0 && hashes.Contains(InfoHashV2));
    }
}
