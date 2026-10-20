using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

// MAM-14 — the download client contract's files of a job: GET api/v2/torrents/files?hash=<TorrentID>, read as an
// allowlist (VendorSamples/QBittorrent/torrent-files-multi.json, 5.2.3). Every request is the sign-in or a GET.
internal sealed partial class QBittorrentAdapter
{
    private static readonly Message NotFilesError = IntegrationAdapterMessages.NotFiles(DisplayName);

    /// <summary>
    /// Linux's <c>PATH_MAX</c>, what a file path column holds (<see cref="PhysicalFileEntity.ServicePath"/>): no
    /// file's path under a torrent's folder is longer, so a longer name is not a path.
    /// </summary>
    private const int FilePathMaximumLength = PhysicalFileEntity.ServicePathMaximumLength;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The session and <c>GET /api/v2/app/version</c> first (<see cref="OpenCheckedSessionAsync"/>), then
    /// <c>GET /api/v2/torrents/files?hash=…</c> with the TorrentID — the id itself, or a 64-character one cut
    /// to 40, as <see cref="FindJobsAsync"/> sends it. qBittorrent answers 404 for a torrent it doesn't have,
    /// which after the version has just answered at the same address is the torrent gone, not a wrong address:
    /// <see cref="IntegrationCallStatus.NotFound"/>. A magnet still fetching its metadata answers <c>[]</c>.
    /// </para>
    /// <para>
    /// An id that isn't 40 or 64 ASCII hexadecimal characters is the caller's bug, thrown as
    /// <see cref="ArgumentException"/> before anything is sent, as <see cref="FindJobsAsync"/> throws.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ListJobFilesAsync(
        IntegrationConnectionSettings settings, string clientJobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if ((LowerHex(clientJobId, V1HashLength) ?? LowerHex(clientJobId, V2HashLength)) is not { } lower)
            throw new ArgumentException("A job id must be 40 or 64 hexadecimal characters.", nameof(clientJobId));

        var path = "api/v2/torrents/files?hash=" + Uri.EscapeDataString(lower[..TorrentIdLength]);
        return CallAsync<IReadOnlyList<ClientJobFile>>(
            settings, (client, sessionSecrets, token) => ReadFilesAsync(client, settings, path, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ReadFilesAsync(
        HttpClient client, IntegrationConnectionSettings settings, string path, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var (call, _, _, failure) = await OpenCheckedSessionAsync(client, settings, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientJobFile>>(failure);

        var (answer, readFailure) = await GetAsync(
            client, settings, call!, path, sessionSecrets, cancellationToken,
            notFound: ConnectionTestResult.Failed(IntegrationCallStatus.NotFound, IntegrationAdapterMessages.TorrentGone(Descriptor.DisplayName)));
        if (readFailure is not null)
            return Failed<IReadOnlyList<ClientJobFile>>(readFailure);

        List<ClientJobFile>? files;
        using (answer)
        using (var document = await TryReadJsonAsync(answer!, cancellationToken))
        {
            files = document is null ? null : ReadFiles(document.RootElement);
        }

        return files is null
            ? IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotFilesError)
            : IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Succeeded(files);
    }

    /// <summary>
    /// Every file of a <c>torrents/files</c> answer, in qBittorrent's order — or <c>null</c> when the body is not
    /// a JSON array of objects each with a usable <c>name</c>, which is how every version answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read as an allowlist: the path (<c>name</c>), the size, the progress and whether the file is wanted
    /// (<c>priority</c> 0 is qBittorrent's "do not download"). <c>is_seed</c>, <c>piece_range</c> and
    /// <c>availability</c> go no further.
    /// </para>
    /// <para>
    /// The path is kept exactly as sent — never trimmed or cleaned like a job's name: it names a real file, and
    /// release names hold double spaces (<c>UkrEng  Sub</c> in the sample) that <see cref="RemoteText.Clean"/>
    /// would collapse. A file without one — missing, not text, empty, or longer than any path — fails the whole
    /// answer rather than being left out, as a torrent without a hash is: a file missing from the list would
    /// read as a download with less in it than it has.
    /// </para>
    /// </remarks>
    private static List<ClientJobFile>? ReadFiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;

        var files = new List<ClientJobFile>();
        foreach (var file in root.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
                return null;
            if (Text(file, "name") is not { Length: > 0 and <= FilePathMaximumLength } name)
                return null;

            files.Add(new ClientJobFile(name, Size(file), Progress(file), IsWanted(file)));
        }

        return files;
    }

    /// <summary>The <c>size</c> in bytes when it is a whole number of zero or more — a movie's is beyond
    /// int32 — otherwise unknown.</summary>
    private static long? Size(JsonElement file) =>
        file.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var bytes) && bytes >= 0
            ? bytes
            : null;

    /// <summary>
    /// Whether qBittorrent downloads the file, from its <c>priority</c>: 0 is "do not download", 1 to 7 a
    /// priority to download at; anything else says nothing.
    /// </summary>
    private static bool? IsWanted(JsonElement file) =>
        file.TryGetProperty("priority", out var priority) && priority.ValueKind == JsonValueKind.Number && priority.TryGetInt32(out var value)
            ? value switch
            {
                0 => false,
                > 0 => true,
                _ => null,
            }
            : null;
}
