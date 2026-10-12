using System.Globalization;
using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.Api.Services.Replacements;

// The reasons: one readable sentence each, as the Jobs page and the job's page show them — a job's reason, a copy's or a
// name's problem. Names and paths quoted with ReplacementText.Quote; a sentence of the disk's or a client's own quoted
// whole after a colon.
public sealed partial class ReplacementQuarantiner
{
    // ── check 5 ──

    private static string OutsideMediaFoldersReason(string path) =>
        $"{ReplacementText.Quote(path)} is no longer inside the Media folders, so it stays where it is.";

    private static string NotTickedReason(string path) =>
        $"{ReplacementText.Quote(path)} is no longer in a folder ticked for replacement, so it stays where it is.";

    private static string DiscClipReason(string path) =>
        $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} is part of a disc folder, which can't be replaced one file at a time.";

    private static string GoneReason(string path) => $"{ReplacementText.Quote(path)} is gone, so there is nothing to move.";

    private static string ChangedReason(string path) =>
        $"{ReplacementText.Quote(path)} changed since it was picked for replacement, so it stays where it is.";

    private static string NotReadReason(string path, string? error) =>
        $"The app couldn't read {ReplacementText.Quote(path)} again, so it stays where it is: {error ?? "the disk gave no reason."}";

    private static string NewFileReason(string path) => $"{ReplacementText.Quote(path)} is the new file's own path, so it stays where it is.";

    private static string PairChangedReason(string path) =>
        $"The new file paired with {ReplacementText.Quote(ReplacementText.FileNameOf(path))} changed or went since it was verified, so nothing moves.";

    // The video's own name is a link, which the listing of its folder leaves out: renaming it would leave the file where it
    // is, and the file it leads to may be anyone's.
    private static string LinkReason(string path) =>
        $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} is a link to another file, and the app moves only a file itself, so it stays where it is.";

    // ── the companions ──

    // The contract gives a sentence with every refusal; this is for one that broke it.
    private static string NotListedReason(string path) => $"The app couldn't list the files beside {ReplacementText.Quote(path)}, so nothing moves.";

    private static string CompanionUnreadableReason(string name) =>
        $"The app couldn't read {ReplacementText.Quote(name)} well enough to tell whether a torrent holds it.";

    // ── check 6 ──

    /// <summary>One sentence per client not read, in the clients' order, and up to two per client read in part — the
    /// torrents it couldn't place, then (MAM-28) the ones its mappings put outside the Media folders: each says what the
    /// app couldn't tell, and why.</summary>
    private static string UncertainReason(IReadOnlyList<ReplacementClientReading> clients) =>
        string.Join(" ", clients.SelectMany(UncertainSentences));

    // One client's sentences: a read in part always has unplaced or outside torrents, or both.
    private static IEnumerable<string> UncertainSentences(ReplacementClientReading client)
    {
        if (client.Status == ReplacementClientCheck.NotRead)
        {
            yield return $"The app couldn't tell whether a torrent in {ReplacementText.Quote(client.ClientName)} holds the old copy: {client.Error}";
            yield break;
        }

        if (client.UnplacedTorrents > 0)
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"The app couldn't place {client.UnplacedTorrents} of {ReplacementText.Quote(client.ClientName)}'s torrents in its folders, so it can't tell whether one of them holds the old copy.");
        if (client.OutsideTorrents > 0)
            yield return OutsideReason(client);
    }

    // MAM-28 — torrents of a client the app sent a replacement to, which its mappings put outside the Media folders, each
    // listing a file that may be a name the move would rename: never looked at there, so never ruled out. The owner's way
    // on is the mappings, under Integrations.
    private static string OutsideReason(ReplacementClientReading client) =>
        client.OutsideTorrents == 1
            ? $"{ReplacementText.Quote(client.ClientName)}'s path mappings put 1 of its torrents outside the Media folders, so the app can't tell whether "
                + "that torrent holds the old copy: check those mappings under Integrations, then press Retry."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{ReplacementText.Quote(client.ClientName)}'s path mappings put {client.OutsideTorrents} of its torrents outside the Media folders, so the app can't tell whether one of them holds the old copy: check those mappings under Integrations, then press Retry.");

    /// <summary>
    /// ADR 0031's sentence for the videos <paramref name="holds"/> keep: every torrent once — told apart by its client and
    /// id, as a cross-seed has the same name —, by client, then torrent name.
    /// </summary>
    private static string HeldReason(IReadOnlyList<ReplacementHold> holds)
    {
        var torrents = TorrentsOf(holds);
        return torrents.Count == 1
            ? $"Still held by the torrent {torrents[0]}: remove that torrent there without its files, then press Retry."
            : $"Still held by the torrents {ReplacementText.List(torrents)}: remove those torrents there without their files, then press Retry.";
    }

    /// <summary>Why a companion <paramref name="holds"/> keep stays where it is, its video moving all the same.</summary>
    private static string CompanionHeldReason(IReadOnlyList<ReplacementHold> holds)
    {
        var torrents = TorrentsOf(holds);
        return torrents.Count == 1
            ? $"Held by the torrent {torrents[0]}, so it stays where it is."
            : $"Held by the torrents {ReplacementText.List(torrents)}, so it stays where it is.";
    }

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

    // ── the move ──

    private static string SwitchedOffWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon(
            "Replacement was switched off while the old copies were moving", ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string LeftTickedWhileMovingReason(string path, (int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon(
            $"{ReplacementText.Quote(path)} left the folders ticked for replacement while the old copies were moving",
            ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string NameNotMovedReason(string path, string error, (int Count, int Total) moved) =>
        ReplacementNameTally.AfterSentence(
            $"{ReplacementText.Quote(ReplacementText.FileNameOf(path))} couldn't be moved into the quarantine: {error}",
            ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    // The contract gives a sentence with every refusal; this is for one that broke it.
    private static string NoSentenceError(FileQuarantineStatus status) => $"the file system refused it ({status}).";

    // A rename the contract answered Swapped: another file took the name's place after the last look, and that file is
    // what moved. No colon in it, as the job's reason quotes it after "couldn't be moved into the quarantine:".
    private static string SwappedWhileMovingProblem(string path) =>
        $"A different file now sits in the quarantine under {ReplacementText.Quote(ReplacementText.FileNameOf(path))}, as another file took its place "
        + "just before the rename, and the old copy's own file wasn't found.";

    // ── a restart's cut ──

    private static string CutWhileMovingReason((int Count, int Total) moved) =>
        ReplacementNameTally.AfterColon("The app stopped while moving the old copies", ReplacementNameTally.InQuarantine(moved.Count, moved.Total));

    private static string CutWhileRestoringReason((int Count, int Total) restored) =>
        ReplacementNameTally.AfterColon(
            "The app stopped while putting the old copies back", ReplacementNameTally.BackWhereTheyWere(restored.Count, restored.Total));

    private static string CutWhileDeletingReason((int Count, int Total) deleted) =>
        ReplacementNameTally.AfterColon(
            "The app stopped while deleting the old copies for good", ReplacementNameTally.Deleted(deleted.Count, deleted.Total));

    private static string LostWhileMovingProblem(string path) =>
        $"The app stopped while moving {ReplacementText.Quote(ReplacementText.FileNameOf(path))}, and found it neither where it was nor in the quarantine.";

    private static string LostWhileRestoringProblem(string path) =>
        $"The app stopped while putting {ReplacementText.Quote(ReplacementText.FileNameOf(path))} back, and found it neither in the quarantine nor where it was.";

    private static string LostWhileDeletingProblem(string path) =>
        $"The app stopped while deleting {ReplacementText.Quote(ReplacementText.FileNameOf(path))} for good, and something else is in its place in the quarantine.";
}
