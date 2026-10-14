using System.Collections.Concurrent;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — the real Transmission answers of <c>VendorSamples/Transmission</c> (MAM-29, 4.1.3 and some on 4.0.6), the
/// requests they answered, and the recorded torrents as a <see cref="FakeTransmissionServer"/> holds them.
/// </summary>
/// <remarks>
/// Each <c>.json</c> is an answer's body indented, its numbers as Transmission wrote them (4.0.6 <c>1</c>, 4.1.3
/// <c>1.0</c>); each <c>.txt</c> a whole answer — status line, headers, page —, read by <c>VendorResponses.Recorded</c>.
/// The session id in the 409s is <see cref="RedactedSessionId"/>. Every answer echoes its request's <c>tag</c>, which
/// each request constant here names. A server gives a torrent the next id when it gets it, whatever its record says:
/// plant Big Buck Bunny first for the recorded ids, 1 and then the magnet's 2.
/// </remarks>
public static class TransmissionSamples
{
    /// <summary><c>session-get</c> of <c>version</c> without a session id, on 4.1.3: <c>409 Conflict</c> with the
    /// session id and <c>X-Transmission-Rpc-Version: 6.0.1</c>, the page saying "session_id".</summary>
    public const string HandshakeResponseFile = "Transmission/handshake-response.txt";

    /// <summary>The same on 4.0.6: the page says "session-id", and the CORS headers stand where 4.1.3 sends the RPC
    /// version.</summary>
    public const string Handshake406ResponseFile = "Transmission/4.0.6-handshake-response.txt";

    /// <summary><c>session-get</c> of <see cref="SessionFields"/>, tag 2, on 4.1.3.</summary>
    public const string SessionGetFile = "Transmission/session-get.json";

    /// <summary>The same on 4.0.6.</summary>
    public const string SessionGet406File = "Transmission/4.0.6-session-get.json";

    /// <summary><c>free-space</c> of <see cref="SampleDownloadDir"/>, tag 3: the total spelled <c>total_size</c>.</summary>
    public const string FreeSpaceDownloadDirFile = "Transmission/free-space-download-dir.json";

    /// <summary><c>free-space</c> of <see cref="MissingFolder"/>, tag 6: "No such file or directory", the total spelled
    /// <c>totalSize</c>.</summary>
    public const string FreeSpaceMissingFolderFile = "Transmission/free-space-missing-folder.json";

    /// <summary><c>torrent-get</c> of <see cref="SmallFields"/> without <c>ids</c>, tag 8, before anything was added:
    /// none.</summary>
    public const string TorrentGetAllFile = "Transmission/torrent-get-all.json";

    /// <summary><c>torrent-get</c> of <see cref="SmallFields"/> with <c>ids</c> a made-up hash, tag 3: none.</summary>
    public const string TorrentGetUnknownHashFile = "Transmission/torrent-get-unknown-hash.json";

    /// <summary><c>torrent-add</c> of Big Buck Bunny's .torrent into <see cref="SampleDownloadDir"/> with
    /// <see cref="Label"/> and <c>paused</c> false, tag 9, on 4.1.3: <c>torrent-added</c>, id 1.</summary>
    public const string TorrentAddAddedFile = "Transmission/torrent-add-added.json";

    /// <summary>The same on 4.0.6, tag 5.</summary>
    public const string TorrentAddAdded406File = "Transmission/4.0.6-torrent-add-added.json";

    /// <summary>The same add again, tag 9: <c>torrent-duplicate</c> of the torrent already there.</summary>
    public const string TorrentAddDuplicateFile = "Transmission/torrent-add-duplicate.json";

    /// <summary><c>torrent-get</c> of <see cref="FullFields"/> for Big Buck Bunny, tag 13, on 4.1.3: downloading
    /// (<see cref="BigBuckBunnyDownloading"/>).</summary>
    public const string TorrentGetDownloadingFile = "Transmission/torrent-get-downloading.json";

    /// <summary>
    /// The same right after the add on 4.0.6, tag 7: verifying (<see cref="BigBuckBunnyVerifying"/>), as every add of a
    /// .torrent whose files aren't there yet is at first, on both versions. A server's own add skips that verify and
    /// lists the torrent downloading at once, so this state is planted, never reached through <c>torrent-add</c>.
    /// </summary>
    public const string TorrentGetVerifying406File = "Transmission/4.0.6-torrent-get-verifying.json";

    /// <summary><c>torrent-add</c> of the base64 of <c>not a torrent</c>, tag 14: "unrecognized info".</summary>
    public const string TorrentAddCorruptMetainfoFile = "Transmission/torrent-add-corrupt-metainfo.json";

    /// <summary><c>torrent-add</c> into <c>relative/path</c>, tag 16: "download directory path is not absolute".</summary>
    public const string TorrentAddRelativeDownloadDirFile = "Transmission/torrent-add-relative-download-dir.json";

    /// <summary><c>torrent-add</c> of <see cref="Magnet"/>, tag 17: <c>torrent-added</c>, id 2, named by its
    /// <c>dn</c>.</summary>
    public const string TorrentAddMagnetAddedFile = "Transmission/torrent-add-magnet-added.json";

    /// <summary><c>torrent-get</c> of <see cref="FullFields"/> for it, tag 18 (<see cref="MagnetWithoutMetadata"/>).</summary>
    public const string TorrentGetMagnetNoMetadataFile = "Transmission/torrent-get-magnet-no-metadata.json";

    /// <summary><c>torrent-add</c> with a stale session id: the handshake's 409, nothing read.</summary>
    public const string TorrentAddStaleSessionResponseFile = "Transmission/torrent-add-stale-session-response.txt";

    /// <summary><c>torrent-get</c> of that torrent right after, tag 21: none — the refused add added nothing.</summary>
    public const string TorrentGetAfterStaleSessionFile = "Transmission/torrent-get-after-stale-session.json";

    /// <summary><c>torrent-get</c> of <see cref="FullFields"/> for Big Buck Bunny once complete, tag 27
    /// (<see cref="BigBuckBunnySeeding"/>).</summary>
    public const string TorrentGetSeedingFile = "Transmission/torrent-get-seeding.json";

    /// <summary>
    /// <c>torrent-stop</c> of Big Buck Bunny, tag 29: <c>success</c>. A write the app never sends (ADR 0014), so a server
    /// answers it, on purpose, not as recorded but with its version's sentence for a method it doesn't know, changing
    /// nothing — a send of it fails loudly instead of seeming to work.
    /// </summary>
    public const string TorrentStopFile = "Transmission/torrent-stop.json";

    /// <summary><c>torrent-get</c> of <see cref="FullFields"/> after the stop, tag 31
    /// (<see cref="BigBuckBunnyStopped"/>).</summary>
    public const string TorrentGetStoppedFile = "Transmission/torrent-get-stopped.json";

    /// <summary><c>session-get</c> with a wrong password, the password set: <c>401 Unauthorized</c> with
    /// <c>WWW-Authenticate: Basic realm="Transmission"</c>.</summary>
    public const string WrongPasswordResponseFile = "Transmission/wrong-password-response.txt";

    /// <summary><c>session-get</c> of <c>version</c>, tag 4, with <c>Host: </c><see cref="NotWhitelistedHost"/>, the
    /// password set: <c>200 OK</c> — no host check with a password.</summary>
    public const string WrongHostAllowedResponseFile = "Transmission/wrong-host-allowed-response.txt";

    /// <summary>What the recordings hold where the session id was.</summary>
    public const string RedactedSessionId = "REDACTED";

    /// <summary>MAM-29's default download folder, where every recorded torrent went.</summary>
    public const string SampleDownloadDir = "/scratch";

    /// <summary>MAM-29's temporary folder, off.</summary>
    public const string SampleIncompleteDir = "/scratch/incomplete";

    /// <summary>The folder <see cref="FreeSpaceMissingFolderFile"/> asked about, which doesn't exist.</summary>
    public const string MissingFolder = "/scratch/mam29-missing-folder";

    /// <summary>The free bytes of <see cref="SampleDownloadDir"/>.</summary>
    public const long SampleFreeBytes = 52_361_691_136;

    /// <summary>The total bytes of <see cref="SampleDownloadDir"/>.</summary>
    public const long SampleTotalBytes = 67_049_664_512;

    /// <summary>The label every recorded add carried.</summary>
    public const string Label = "MAM29 Samples";

    /// <summary>The Host the wrong-host request named.</summary>
    public const string NotWhitelistedHost = "mam29-not-whitelisted.invalid";

    /// <summary>Big Buck Bunny's v1 info hash (webtorrent.io's .torrent): three files under its folder.</summary>
    public const string BigBuckBunnyHash = "dd8255ecdc7ca55fb0bbf81323d87062db1f6d1c";

    /// <summary>Its name, which is its folder's too.</summary>
    public const string BigBuckBunnyName = "Big Buck Bunny";

    /// <summary>Its bytes in all.</summary>
    public const long BigBuckBunnySize = 276_445_467;

    /// <summary>When the 4.1.3 Transmission added it.</summary>
    public const long BigBuckBunnyAddedDate = 1_791_903_931;

    /// <summary>When it was complete there.</summary>
    public const long BigBuckBunnyDoneDate = 1_791_903_968;

    /// <summary>When the 4.0.6 Transmission added it.</summary>
    public const long BigBuckBunny406AddedDate = 1_791_904_497;

    /// <summary>The made-up hash of the magnet without metadata.</summary>
    public const string MagnetHash = "d1d1735ea40e68f43caa49ee04a0db5901651e6a";

    /// <summary>Its <c>dn</c>.</summary>
    public const string MagnetName = "mam29-no-metadata";

    /// <summary>The magnet link itself: no tracker, its hash and its name.</summary>
    public const string Magnet = $"magnet:?xt=urn:btih:{MagnetHash}&dn={MagnetName}";

    /// <summary>When it was added.</summary>
    public const long MagnetAddedDate = 1_791_903_940;

    private static readonly ConcurrentDictionary<string, string> Texts = new(StringComparer.Ordinal);

    /// <summary>The <c>session-get</c> fields <see cref="SessionGetFile"/> asked for.</summary>
    public static IReadOnlyList<string> SessionFields { get; } =
    [
        "version", "rpc-version", "rpc-version-minimum", "rpc-version-semver", "download-dir", "incomplete-dir",
        "incomplete-dir-enabled", "start-added-torrents", "rename-partial-files",
    ];

    /// <summary>The <c>torrent-get</c> fields <see cref="TorrentGetAllFile"/> and <see cref="TorrentGetUnknownHashFile"/>
    /// asked for.</summary>
    public static IReadOnlyList<string> SmallFields { get; } = ["id", "name", "hashString", "status", "percentDone", "downloadDir", "labels"];

    /// <summary>The <c>torrent-get</c> fields the recordings of one torrent asked for.</summary>
    public static IReadOnlyList<string> FullFields { get; } =
    [
        "id", "name", "hashString", "downloadDir", "status", "percentDone", "leftUntilDone", "sizeWhenDone", "totalSize",
        "isFinished", "isStalled", "error", "errorString", "labels", "metadataPercentComplete", "addedDate", "doneDate", "files",
        "fileStats",
    ];

    /// <summary>The recorded file <paramref name="file"/> (one of the constants here) as text, read once.</summary>
    public static string Read(string file) =>
        Texts.GetOrAdd(file, path => File.ReadAllText(Path.Combine(RepositoryPaths.VendorSamples, path)));

    /// <summary>Big Buck Bunny's files with <paramref name="bytesCompleted"/> of each, in its order.</summary>
    public static IReadOnlyList<FakeTransmissionFile> BigBuckBunnyFiles(long subtitles, long movie, long poster) =>
    [
        new($"{BigBuckBunnyName}/Big Buck Bunny.en.srt", 140, subtitles),
        new($"{BigBuckBunnyName}/Big Buck Bunny.mp4", 276_134_947, movie),
        new($"{BigBuckBunnyName}/poster.jpg", 310_380, poster),
    ];

    /// <summary>Big Buck Bunny as <see cref="TorrentGetDownloadingFile"/> lists it: downloading, 0.9 % there.</summary>
    public static FakeTransmissionTorrent BigBuckBunnyDownloading() =>
        BigBuckBunny(BigBuckBunnyFiles(140, 2_473_844, 0)) with { Status = 4, AddedDate = BigBuckBunnyAddedDate };

    /// <summary>Big Buck Bunny as <see cref="TorrentGetSeedingFile"/> lists it: complete and seeding.</summary>
    public static FakeTransmissionTorrent BigBuckBunnySeeding() =>
        BigBuckBunny(BigBuckBunnyFiles(140, 276_134_947, 310_380)) with
        {
            Status = 6,
            AddedDate = BigBuckBunnyAddedDate,
            DoneDate = BigBuckBunnyDoneDate,
        };

    /// <summary>Big Buck Bunny as <see cref="TorrentGetStoppedFile"/> lists it: complete and stopped.</summary>
    public static FakeTransmissionTorrent BigBuckBunnyStopped() => BigBuckBunnySeeding() with { Status = 0 };

    /// <summary>Big Buck Bunny as <see cref="TorrentGetVerifying406File"/> lists it on 4.0.6: verifying, stalled, nothing
    /// there yet — the state a server's own <c>torrent-add</c> skips.</summary>
    public static FakeTransmissionTorrent BigBuckBunnyVerifying() =>
        BigBuckBunny(BigBuckBunnyFiles(0, 0, 0)) with { Status = 2, AddedDate = BigBuckBunny406AddedDate, IsStalled = true };

    /// <summary>The magnet as <see cref="TorrentGetMagnetNoMetadataFile"/> lists it: downloading, no metadata, no files,
    /// nothing left — and not complete.</summary>
    public static FakeTransmissionTorrent MagnetWithoutMetadata() =>
        new(2, MagnetHash, MagnetName, SampleDownloadDir, 4, 0, 0, 0, 0, 0, 0, "", [Label], MagnetAddedDate, 0, false, false);

    private static FakeTransmissionTorrent BigBuckBunny(IReadOnlyList<FakeTransmissionFile> files) =>
        new FakeTransmissionTorrent(1, BigBuckBunnyHash, BigBuckBunnyName, SampleDownloadDir, 0, 0, 0, 0, 0, 1, 0, "", [Label], 0, 0, false, false)
            .WithFiles(files);
}
