using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

// MAM-30 — the download client contract's files of a job: torrent-get of one torrent's files and fileStats, read as an
// allowlist (VendorSamples/Transmission/torrent-get-seeding.json). Every request is session-get or torrent-get.
internal sealed partial class TransmissionAdapter
{
    private static readonly Message NotFilesError = IntegrationAdapterMessages.NotFiles(DisplayName);

    /// <summary>
    /// Linux's <c>PATH_MAX</c>, what a file path column holds (<see cref="PhysicalFileEntity.ServicePath"/>): no file's
    /// path under a torrent's folder is longer, so a longer name is not a path.
    /// </summary>
    private const int FilePathMaximumLength = PhysicalFileEntity.ServicePathMaximumLength;

    /// <summary>What a torrent's files are read from: its hash, to be sure it is the one asked for, whether it has its
    /// metadata, and the two lists, whose entries match by position.</summary>
    private static readonly string[] FileFields = ["hashString", "metadataPercentComplete", "files", "fileStats"];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>session-get</c> first (<see cref="OpenAsync"/>), then one <c>torrent-get</c> of <see cref="FileFields"/> with
    /// <c>ids</c> the job's hash. Transmission lists none for a torrent it no longer has, which after the session has just
    /// answered at the same address is the torrent gone: <see cref="IntegrationCallStatus.NotFound"/>. A magnet still
    /// fetching its metadata lists no files: none, as the contract says.
    /// </para>
    /// <para>
    /// A 64-character id names no Transmission torrent — Transmission names each by its v1 hash, which is the id it gives a
    /// job — so it is answered as gone without a request. Any other id that isn't 40 ASCII hexadecimal characters is the
    /// caller's bug, thrown as <see cref="ArgumentException"/> before anything is sent.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ListJobFilesAsync(
        IntegrationConnectionSettings settings, string clientJobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (LowerHex(clientJobId, V1HashLength) is not { } hash)
        {
            return LowerHex(clientJobId, V2HashLength) is null
                ? throw new ArgumentException("A job id must be 40 or 64 hexadecimal characters.", nameof(clientJobId))
                : Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Failed(IntegrationCallStatus.NotFound, IntegrationAdapterMessages.TorrentGone(Descriptor.DisplayName)));
        }

        return CallAsync<IReadOnlyList<ClientJobFile>>(
            settings, (client, sessionSecrets, token) => ReadFilesAsync(client, settings, hash, sessionSecrets, token), cancellationToken);
    }

    private async Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ReadFilesAsync(
        HttpClient client, IntegrationConnectionSettings settings, string hash, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var call = NewCall(settings, isTest: false, sessionSecrets);
        var (_, failure) = await OpenAsync(client, call, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientJobFile>>(failure);

        var arguments = new JsonObject { ["ids"] = Names([hash]), ["fields"] = Names(FileFields) };
        var (answer, readFailure) = await SendRpcAsync(client, call, TorrentGetMethod, arguments, sessionSecrets, cancellationToken);
        if (readFailure is not null)
            return Failed<IReadOnlyList<ClientJobFile>>(readFailure);
        if (!answer!.IsSuccess)
            return Failed<IReadOnlyList<ClientJobFile>>(RequestRefused(answer, settings, sessionSecrets));

        switch (TorrentsOf(answer.Arguments))
        {
            case []:
                return IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Failed(IntegrationCallStatus.NotFound, IntegrationAdapterMessages.TorrentGone(Descriptor.DisplayName));
            case [var torrent] when LowerHex(Text(torrent, "hashString"), V1HashLength) == hash:
                if (Fraction(torrent, "metadataPercentComplete") is not >= 1)
                    return IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Succeeded([]);
                return ReadFiles(torrent) is { } files
                    ? IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Succeeded(files)
                    : IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotFilesError);
            default:
                // Not a list of torrents, more than the one asked for, or another one: not an answer to this question.
                return IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotFilesError);
        }
    }

    /// <summary>
    /// Every file of a torrent, in Transmission's order — or <c>null</c> when its <c>files</c> and <c>fileStats</c> aren't
    /// two lists of objects of one length, each file with a usable <c>name</c>, which is how every version answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read as an allowlist: the path (<c>files[].name</c>), the size (<c>length</c>), the progress (<c>bytesCompleted</c>
    /// of the length; an empty file is whole) and whether it is wanted (<c>fileStats[].wanted</c>). 4.1's
    /// <c>begin_piece</c> and <c>end_piece</c> and the priority go no further.
    /// </para>
    /// <para>
    /// The path is kept exactly as sent — relative to the torrent's <c>downloadDir</c>, its own folder first for a torrent
    /// of several files, already cleaned the way Transmission writes it, and never with the <c>.part</c> an unfinished
    /// file carries on disk. A file without one fails the whole answer rather than being left out: a file missing from the
    /// list would read as a download with less in it than it has.
    /// </para>
    /// </remarks>
    private static List<ClientJobFile>? ReadFiles(JsonElement torrent)
    {
        if (!torrent.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array
            || !torrent.TryGetProperty("fileStats", out var stats) || stats.ValueKind != JsonValueKind.Array
            || files.GetArrayLength() != stats.GetArrayLength())
            return null;

        var read = new List<ClientJobFile>();
        for (var index = 0; index < files.GetArrayLength(); index++)
        {
            var file = files[index];
            var stat = stats[index];
            if (file.ValueKind != JsonValueKind.Object || stat.ValueKind != JsonValueKind.Object)
                return null;
            if (Text(file, "name") is not { Length: > 0 and <= FilePathMaximumLength } name)
                return null;

            var length = Whole(file, "length") is { } size && size >= 0 ? size : (long?)null;
            read.Add(new ClientJobFile(name, length, FileProgress(length, Whole(file, "bytesCompleted")), Flag(stat, "wanted")));
        }

        return read;
    }

    /// <summary>The share of a file Transmission has, from 0 to 1 — an empty file is whole — or <c>null</c> when it
    /// doesn't say.</summary>
    private static double? FileProgress(long? length, long? bytesCompleted)
    {
        if (length == 0)
            return 1;
        if (length is not { } total || bytesCompleted is not { } completed || completed < 0)
            return null;
        return Math.Min(1, (double)completed / total);
    }
}
