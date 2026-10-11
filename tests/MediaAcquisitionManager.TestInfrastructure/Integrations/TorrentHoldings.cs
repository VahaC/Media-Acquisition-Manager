using System.Security.Cryptography;
using System.Text;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-27 — a torrent that holds an old copy (ADR 0031), for the tests of what the owner does about one: until now each
/// replacement class that needed one wrote it out for itself.
/// </summary>
public static class TorrentHoldings
{
    /// <summary>
    /// A torrent named <paramref name="name"/> of qBittorrent's Movies category — saving to its <c>/downloads/movies</c>,
    /// the app's <c>/media/movies</c>, unless <paramref name="savePath"/> says otherwise — in <paramref name="state"/>, with
    /// one file at <paramref name="file"/> (its root folder included) of <paramref name="size"/>, wholly downloaded. Its
    /// info hash is <paramref name="hash"/>, or one made from its name, so two holdings of one test never share one.
    /// </summary>
    public static FakeQBittorrentTorrent Holding(
        string name, string file, long size, string state = "stalledUP", string savePath = "/downloads/movies", string? hash = null) =>
        FakeQBittorrentTorrent.Of(
                new TorrentInfoHashes(hash ?? Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(name))), null),
                name,
                "Movies",
                state,
                savePath: savePath)
            with { Files = [new FakeQBittorrentFile(file, size, 1)] };
}
