using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-40 — check 6 (ADR 0031), as a replacement's mover (MAM-26) and the owner's Remove both make it right before a move:
/// one read of every download client the replacement code consults for every name the move would rename, and the
/// sentences of what it found — a client not read or read in part stops everything, a torrent holding a video stops it,
/// a torrent holding a companion keeps that companion where it is.
/// </summary>
/// <remarks>
/// <para>Reads the clients' listings only; nothing is ever written to a client.</para>
/// <para>
/// MAM-47 (ADR 0042) — the sentences are messages of the <c>replacements.quarantine.held</c> and <c>.uncertain</c> keys,
/// a key per <see cref="QuarantineWording"/>: the copy's name and the button the owner presses again are the template's,
/// so each language names the button as its web app labels it.
/// </para>
/// </remarks>
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

    /// <summary>
    /// One sentence per client not read, in the clients' order, and up to two per client read in part — the torrents it
    /// couldn't place, then (MAM-28) the ones its mappings put outside the Media folders: each says what the app couldn't
    /// tell, and why. <paramref name="clients"/> holds at least one.
    /// </summary>
    /// <remarks>
    /// MAM-47 — each sentence nests the next as its <c>more</c> (a <c>-more</c> key), read one after another as the
    /// sentences were joined: a whole sentence per shape, never a wording that opens with a value only to glue two
    /// together, which the startup converter could match against every stored text (WP4 review N4).
    /// </remarks>
    public static Message UncertainReason(IReadOnlyList<ReplacementClientReading> clients, QuarantineWording wording)
    {
        Message? reason = null;
        foreach (var sentence in clients.SelectMany(UncertainSentences).Reverse())
            reason = sentence(wording, reason);
        return reason ?? throw new ArgumentException("Only a client the read left uncertain has a sentence.", nameof(clients));
    }

    /// <summary>
    /// ADR 0031's sentence for the videos <paramref name="holds"/> keep: every torrent once — told apart by its client and
    /// id, as a cross-seed has the same name —, by client, then torrent name.
    /// </summary>
    public static Message HeldReason(IReadOnlyList<ReplacementHold> holds, QuarantineWording wording) =>
        (wording, TorrentsOf(holds)) switch
        {
            (QuarantineWording.Replacement, [var torrent]) => Message.Of("replacements.quarantine.held.by-torrent-replacement", ("torrent", torrent)),
            (QuarantineWording.Replacement, var torrents) =>
                Message.Of("replacements.quarantine.held.by-torrents-replacement", ("torrents", new ListValue(torrents))),
            (_, [var torrent]) => Message.Of("replacements.quarantine.held.by-torrent-removal", ("torrent", torrent)),
            (_, var torrents) => Message.Of("replacements.quarantine.held.by-torrents-removal", ("torrents", new ListValue(torrents))),
        };

    /// <summary>Why a companion <paramref name="holds"/> keep stays where it is, its video moving all the same.</summary>
    public static Message CompanionHeldReason(IReadOnlyList<ReplacementHold> holds) => TorrentsOf(holds) switch
    {
        [var torrent] => Message.Of("replacements.quarantine.held.companion-by-torrent", ("torrent", torrent)),
        var torrents => Message.Of("replacements.quarantine.held.companion-by-torrents", ("torrents", new ListValue(torrents))),
    };

    // One client's sentences, each to be given the sentence after it: a read in part always has unplaced or outside
    // torrents, or both.
    private static IEnumerable<Func<QuarantineWording, Message?, Message>> UncertainSentences(ReplacementClientReading client)
    {
        if (client.Status == ReplacementClientCheck.NotRead)
        {
            yield return (wording, more) => NotReadSentence(client, wording, more);
            yield break;
        }

        if (client.UnplacedTorrents > 0)
            yield return (wording, more) => UnplacedSentence(client, wording, more);
        if (client.OutsideTorrents > 0)
            yield return (wording, more) => OutsideSentence(client, wording, more);
    }

    // The client's own sentence after the colon: why it wasn't read. The reader always gives one; should it ever not, the
    // answer it couldn't read is the honest why, never an empty colon.
    private static Message NotReadSentence(ReplacementClientReading client, QuarantineWording wording, Message? more)
    {
        var name = MessageText.Clean(client.ClientName);
        var error = client.Error ?? IntegrationAdapterMessages.UnreadableAnswer(client.ClientName);
        return (wording, more) switch
        {
            (QuarantineWording.Replacement, null) =>
                Message.Of("replacements.quarantine.uncertain.not-read-replacement", ("client", name), ("error", error)),
            (QuarantineWording.Replacement, { } next) =>
                Message.Of("replacements.quarantine.uncertain.not-read-replacement-more", ("client", name), ("error", error), ("more", next)),
            (_, null) => Message.Of("replacements.quarantine.uncertain.not-read-removal", ("client", name), ("error", error)),
            (_, { } next) =>
                Message.Of("replacements.quarantine.uncertain.not-read-removal-more", ("client", name), ("error", error), ("more", next)),
        };
    }

    private static Message UnplacedSentence(ReplacementClientReading client, QuarantineWording wording, Message? more)
    {
        var (name, count) = (MessageText.Clean(client.ClientName), client.UnplacedTorrents);
        return (wording, more) switch
        {
            (QuarantineWording.Replacement, null) =>
                Message.Of("replacements.quarantine.uncertain.unplaced-replacement", ("client", name), ("count", count)),
            (QuarantineWording.Replacement, { } next) =>
                Message.Of("replacements.quarantine.uncertain.unplaced-replacement-more", ("client", name), ("count", count), ("more", next)),
            (_, null) => Message.Of("replacements.quarantine.uncertain.unplaced-removal", ("client", name), ("count", count)),
            (_, { } next) =>
                Message.Of("replacements.quarantine.uncertain.unplaced-removal-more", ("client", name), ("count", count), ("more", next)),
        };
    }

    // MAM-28 — torrents of a client the app sent a replacement to, which its mappings put outside the Media folders, each
    // listing a file that may be a name the move would rename: never looked at there, so never ruled out. The owner's way
    // on is the mappings, under Integrations.
    private static Message OutsideSentence(ReplacementClientReading client, QuarantineWording wording, Message? more)
    {
        var (name, count) = (MessageText.Clean(client.ClientName), client.OutsideTorrents);
        return (wording, more) switch
        {
            (QuarantineWording.Replacement, null) =>
                Message.Of("replacements.quarantine.uncertain.outside-replacement", ("client", name), ("count", count)),
            (QuarantineWording.Replacement, { } next) =>
                Message.Of("replacements.quarantine.uncertain.outside-replacement-more", ("client", name), ("count", count), ("more", next)),
            (_, null) => Message.Of("replacements.quarantine.uncertain.outside-removal", ("client", name), ("count", count)),
            (_, { } next) =>
                Message.Of("replacements.quarantine.uncertain.outside-removal-more", ("client", name), ("count", count), ("more", next)),
        };
    }

    // "“Alien.1979” in “qBittorrent”", each torrent once.
    private static List<Message> TorrentsOf(IReadOnlyList<ReplacementHold> holds) =>
    [
        .. holds
            .DistinctBy(hold => (hold.ClientName, hold.TorrentId))
            .OrderBy(hold => hold.ClientName, StringComparer.Ordinal)
            .ThenBy(hold => hold.TorrentName, StringComparer.Ordinal)
            .ThenBy(hold => hold.TorrentId, StringComparer.Ordinal)
            .Select(hold => Message.Of(
                "replacements.quarantine.held.torrent-clause",
                ("torrent", MessageText.Clean(hold.TorrentName)),
                ("client", MessageText.Clean(hold.ClientName)))),
    ];
}
