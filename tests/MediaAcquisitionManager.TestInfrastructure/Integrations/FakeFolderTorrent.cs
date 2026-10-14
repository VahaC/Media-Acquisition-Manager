using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one torrent <see cref="FakeFolderDownloadClient"/> has: as a Transmission lists one, by its v1 hash, in the
/// folder it saves to, with its files relative to that folder (the torrent's own folder first for a torrent of several).
/// </summary>
/// <param name="Hash">Its v1 info hash, 40 hexadecimal characters: its id, which never changes.</param>
/// <param name="Name">Its name, which is its own folder or its one file below <paramref name="DownloadDir"/>.</param>
/// <param name="DownloadDir">The folder it saves to, as the client sees it.</param>
/// <param name="Files">Its files; <c>null</c> for a magnet still fetching its metadata, whose files nobody knows yet.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Progress">How far it has come, from 0 to 1.</param>
public sealed record FakeFolderTorrent(
    string Hash,
    string Name,
    string DownloadDir,
    IReadOnlyList<ClientJobFile>? Files,
    ClientJobState State = ClientJobState.Seeding,
    double? Progress = 1)
{
    /// <summary>A finished torrent of one file, <paramref name="name"/>, of <paramref name="sizeBytes"/>, in
    /// <paramref name="downloadDir"/>.</summary>
    public static FakeFolderTorrent OneFile(string hash, string name, string downloadDir, long sizeBytes) =>
        new(hash, name, downloadDir, [new ClientJobFile(name, sizeBytes, 1, true)]);

    /// <summary>The job the client lists for it: its hash as its id, and — once it has its metadata — its content where its
    /// name puts it below its folder.</summary>
    public ClientJob ToJob() =>
        new(
            Hash,
            Name,
            State,
            Progress,
            DownloadDir,
            Files is not null && NormalizedPath.TryParse(DownloadDir, out var folder, out _) ? folder.Append([Name]).ToString() : null,
            [Hash],
            DownloadDir);
}
