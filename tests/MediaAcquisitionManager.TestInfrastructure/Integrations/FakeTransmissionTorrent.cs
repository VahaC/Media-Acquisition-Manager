using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one torrent a <see cref="FakeTransmissionServer"/> has: what <c>torrent-get</c> lists for it, in
/// Transmission's own legacy field names, and its files (<see cref="Files"/>).
/// </summary>
/// <param name="Id">Its <c>id</c>: a number the server hands out from 1, in the order it gets torrents, as Transmission
/// does for the life of its process.</param>
/// <param name="HashString">Its <c>hashString</c>: the v1 info hash, 40 lower-case hexadecimal characters — a hybrid
/// torrent's too. Transmission names every torrent by it, and takes none without one.</param>
/// <param name="Name">Its <c>name</c>: the torrent's name as its metadata gives it, never cleaned; for a magnet still
/// fetching its metadata, the link's <c>dn</c>, or the hash without one.</param>
/// <param name="DownloadDir">Its <c>downloadDir</c>, exactly as <c>torrent-add</c>'s <c>download-dir</c> gave it.</param>
/// <param name="Status">Its <c>status</c>: 0 stopped, 1 queued to verify, 2 verifying, 3 queued to download,
/// 4 downloading, 5 queued to seed, 6 seeding.</param>
/// <param name="PercentDone">Its <c>percentDone</c>, from 0 to 1, over the wanted files only.</param>
/// <param name="LeftUntilDone">Its <c>leftUntilDone</c>: the wanted bytes still missing — 0 for a magnet without
/// metadata.</param>
/// <param name="SizeWhenDone">Its <c>sizeWhenDone</c>: the wanted bytes in all.</param>
/// <param name="TotalSize">Its <c>totalSize</c>: every file's bytes.</param>
/// <param name="MetadataPercentComplete">Its <c>metadataPercentComplete</c>: 1 once Transmission has the metadata.</param>
/// <param name="Error">Its <c>error</c>: 0 none, 1 a tracker's warning, 2 a tracker's error, 3 a local error such as a
/// full disk.</param>
/// <param name="ErrorString">Its <c>errorString</c>; <c>""</c> without an error.</param>
/// <param name="Labels">Its <c>labels</c>, as Transmission stored them.</param>
/// <param name="AddedDate">Its <c>addedDate</c>, in Unix seconds.</param>
/// <param name="DoneDate">Its <c>doneDate</c>, in Unix seconds; 0 until it is complete.</param>
/// <param name="IsFinished">Its <c>isFinished</c>: the seeding limit reached — not "complete", which it never says.</param>
/// <param name="IsStalled">Its <c>isStalled</c>.</param>
public sealed record FakeTransmissionTorrent(
    int Id,
    string HashString,
    string Name,
    string DownloadDir,
    int Status,
    double PercentDone,
    long LeftUntilDone,
    long SizeWhenDone,
    long TotalSize,
    double MetadataPercentComplete,
    int Error,
    string ErrorString,
    IReadOnlyList<string> Labels,
    long AddedDate,
    long DoneDate,
    bool IsFinished,
    bool IsStalled)
{
    /// <summary>The piece length a torrent has unless a test or its .torrent says otherwise: 256 KiB, the recorded Big
    /// Buck Bunny's, whose <c>begin_piece</c> and <c>end_piece</c> it gives.</summary>
    public const long DefaultPieceLength = 256 * 1024;

    /// <summary>Its files, in the torrent's order; none for a magnet still fetching its metadata.</summary>
    public IReadOnlyList<FakeTransmissionFile> Files { get; init; } = [];

    /// <summary>Its piece length, which 4.1's <c>begin_piece</c> and <c>end_piece</c> of each file are counted in.</summary>
    public long PieceLength { get; init; } = DefaultPieceLength;

    /// <summary>Whether Transmission has the torrent's metadata, as <c>metadataPercentComplete</c> says.</summary>
    public bool HasMetadata => MetadataPercentComplete >= 1;

    /// <summary>
    /// A torrent named by <paramref name="hashes"/>' v1 hash, in <paramref name="downloadDir"/>, with the metadata and
    /// <paramref name="files"/> (none when not given): its sizes and progress worked out from the files
    /// (<see cref="WithFiles"/>), seeding when every wanted byte is there and downloading otherwise. Its id and dates
    /// are the server's to give.
    /// </summary>
    public static FakeTransmissionTorrent Of(
        TorrentInfoHashes hashes,
        string name,
        string downloadDir = FakeTransmissionServer.DefaultDownloadDir,
        IReadOnlyList<FakeTransmissionFile>? files = null,
        IReadOnlyList<string>? labels = null)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        if (hashes.V1 is null)
            throw new ArgumentException("Transmission names a torrent by its v1 info hash, and takes none without one.", nameof(hashes));

        var torrent = new FakeTransmissionTorrent(
            0, hashes.V1, name, downloadDir, 0, 0, 0, 0, 0, 1, 0, "", labels ?? [], 0, 0, false, false).WithFiles(files ?? []);
        return torrent with { Status = torrent.LeftUntilDone == 0 ? 6 : 4 };
    }

    /// <summary>
    /// This torrent with the metadata and <paramref name="files"/>: <c>totalSize</c> every file's bytes,
    /// <c>sizeWhenDone</c> and <c>leftUntilDone</c> the wanted files' bytes and those still missing, and
    /// <c>percentDone</c> the share of the wanted bytes it has — 1 when nothing is wanted — as the <c>float</c>
    /// Transmission keeps it in, which is why the recorded 0.0089 reads 0.00894926581531763.
    /// </summary>
    public FakeTransmissionTorrent WithFiles(IReadOnlyList<FakeTransmissionFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sizeWhenDone = files.Where(file => file.Wanted).Sum(file => file.Length);
        var leftUntilDone = files.Where(file => file.Wanted).Sum(file => file.Length - file.BytesCompleted);
        var percentDone = sizeWhenDone == 0 ? 1 : (double)(float)((double)(sizeWhenDone - leftUntilDone) / sizeWhenDone);
        return this with
        {
            Files = files,
            MetadataPercentComplete = 1,
            TotalSize = files.Sum(file => file.Length),
            SizeWhenDone = sizeWhenDone,
            LeftUntilDone = leftUntilDone,
            PercentDone = percentDone,
        };
    }
}
