using System.Globalization;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Replacements;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — check 6 (ADR 0031), as a replacement's mover (MAM-26) and the owner's Remove both make it right before a move:
/// one read of every download client the replacement code consults for every name the move would rename, and the
/// sentences of what it found — a client not read or read in part stops everything, a torrent holding a video stops it,
/// a torrent holding a companion keeps that companion where it is.
/// </summary>
/// <remarks>Reads the clients' listings only; nothing is ever written to a client.</remarks>
internal static class QuarantineHoldingCheck
{
    /// <summary>
    /// The torrents holding each of <paramref name="targets"/>, and how each client was read, inside
    /// <paramref name="allowedRoots"/>: every client whose mappings reach them, and (MAM-28) every client the app sent a
    /// replacement to that still exists, whatever its mappings say now — one deleted or pointed outside the Media folders
    /// since may hide a name it lists. A client deleted and added again has a new id, and a send that never reached its
    /// client has no id there.
    /// </summary>
    public static async Task<ReplacementHoldings> ReadAsync(
        ApplicationDbContext db,
        IReplacementHoldingReader holdingReader,
        IReadOnlyList<ReplacementHoldingTarget> targets,
        IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken)
    {
        var libraryClients = await db.ReplacementJobs
            .AsNoTracking()
            .Join(db.DownloadJobs.AsNoTracking(), replacement => replacement.DownloadJobId, download => download.Id, (replacement, download) => download)
            .Where(download => download.ClientJobId != null && download.DownloadClientConnectionId != null)
            .Select(download => download.DownloadClientConnectionId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        return await holdingReader.ReadAsync(targets, allowedRoots, libraryClients, cancellationToken);
    }

    /// <summary>The clients <paramref name="holdings"/> didn't read whole: anything uncertain is never an all-clear (ADR
    /// 0031).</summary>
    public static IReadOnlyList<ReplacementClientReading> UncertainOf(ReplacementHoldings holdings) =>
        [.. holdings.Clients.Where(client => client.Status != ReplacementClientCheck.Read)];

    /// <summary>The holds on <paramref name="path"/> by a torrent's own name — its path, or the file reached through a link
    /// —; none for a second name, which may move.</summary>
    public static List<ReplacementHold> HoldsOf(ReplacementHoldings holdings, NormalizedPath path) =>
        [.. (holdings.Holds.GetValueOrDefault(path) ?? []).Where(hold => !hold.SecondName)];

    /// <summary>One sentence per client not read, in the clients' order, and up to two per client read in part — the
    /// torrents it couldn't place, then (MAM-28) the ones its mappings put outside the Media folders: each says what the
    /// app couldn't tell, and why.</summary>
    public static string UncertainReason(IReadOnlyList<ReplacementClientReading> clients, QuarantineWording wording) =>
        string.Join(" ", clients.SelectMany(client => UncertainSentences(client, wording)));

    /// <summary>
    /// ADR 0031's sentence for the videos <paramref name="holds"/> keep: every torrent once — told apart by its client and
    /// id, as a cross-seed has the same name —, by client, then torrent name.
    /// </summary>
    public static string HeldReason(IReadOnlyList<ReplacementHold> holds, QuarantineWording wording)
    {
        var torrents = TorrentsOf(holds);
        return torrents.Count == 1
            ? $"Still held by the torrent {torrents[0]}: remove that torrent there without its files, then press {wording.PressAgain}."
            : $"Still held by the torrents {ReplacementText.List(torrents)}: remove those torrents there without their files, then press {wording.PressAgain}.";
    }

    /// <summary>Why a companion <paramref name="holds"/> keep stays where it is, its video moving all the same.</summary>
    public static string CompanionHeldReason(IReadOnlyList<ReplacementHold> holds)
    {
        var torrents = TorrentsOf(holds);
        return torrents.Count == 1
            ? $"Held by the torrent {torrents[0]}, so it stays where it is."
            : $"Held by the torrents {ReplacementText.List(torrents)}, so it stays where it is.";
    }

    // One client's sentences: a read in part always has unplaced or outside torrents, or both.
    private static IEnumerable<string> UncertainSentences(ReplacementClientReading client, QuarantineWording wording)
    {
        if (client.Status == ReplacementClientCheck.NotRead)
        {
            yield return $"The app couldn't tell whether a torrent in {ReplacementText.Quote(client.ClientName)} holds {wording.Copy}: {client.Error}";
            yield break;
        }

        if (client.UnplacedTorrents > 0)
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"The app couldn't place {client.UnplacedTorrents} of {ReplacementText.Quote(client.ClientName)}'s torrents in its folders, so it can't tell whether one of them holds {wording.Copy}.");
        if (client.OutsideTorrents > 0)
            yield return OutsideReason(client, wording);
    }

    // MAM-28 — torrents of a client the app sent a replacement to, which its mappings put outside the Media folders, each
    // listing a file that may be a name the move would rename: never looked at there, so never ruled out. The owner's way
    // on is the mappings, under Integrations.
    private static string OutsideReason(ReplacementClientReading client, QuarantineWording wording) =>
        client.OutsideTorrents == 1
            ? $"{ReplacementText.Quote(client.ClientName)}'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether "
                + $"that torrent holds {wording.Copy}: check those mappings under Integrations, then press {wording.PressAgain}."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{ReplacementText.Quote(client.ClientName)}'s path mappings put {client.OutsideTorrents} of its torrents outside the Media folders, so the app can't tell whether one of them holds {wording.Copy}: check those mappings under Integrations, then press {wording.PressAgain}.");

    // "“Alien.1979” in “qBittorrent”", each torrent once.
    private static List<string> TorrentsOf(IReadOnlyList<ReplacementHold> holds) =>
    [
        .. holds
            .DistinctBy(hold => (hold.ClientName, hold.TorrentId))
            .OrderBy(hold => hold.ClientName, StringComparer.Ordinal)
            .ThenBy(hold => hold.TorrentName, StringComparer.Ordinal)
            .ThenBy(hold => hold.TorrentId, StringComparer.Ordinal)
            .Select(hold => $"{ReplacementText.Quote(hold.TorrentName)} in {ReplacementText.Quote(hold.ClientName)}"),
    ];
}
