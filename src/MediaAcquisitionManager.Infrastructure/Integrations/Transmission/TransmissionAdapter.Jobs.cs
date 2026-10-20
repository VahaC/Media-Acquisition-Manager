using System.Text.Json;
using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

// MAM-30 — the download client contract's job lookups: the torrents Transmission lists under the info hashes the app
// worked out from a payload, and the torrents of one folder, read from torrent-get as an allowlist. Every request is
// session-get or torrent-get.
internal sealed partial class TransmissionAdapter
{
    private static readonly Message NotTorrentsError = IntegrationAdapterMessages.NotTorrents(DisplayName);

    /// <summary>Longer than any release name worth showing, short enough that a hostile name can't fill a job's row.</summary>
    private const int JobNameMaximumLength = 1000;

    /// <summary>
    /// What a job is read from, and nothing more: never <c>id</c>, which Transmission numbers afresh every time it starts;
    /// never <c>labels</c>, which decide nothing (ADR 0039); never the trackers or the magnet link, which can carry the
    /// owner's passkey; never <c>errorString</c>, which no field of a job carries.
    /// </summary>
    private static readonly string[] JobFields =
        ["hashString", "name", "status", "error", "percentDone", "leftUntilDone", "metadataPercentComplete", "downloadDir"];

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>session-get</c> first (<see cref="OpenAsync"/>), then one <c>torrent-get</c> of <see cref="JobFields"/> with
    /// <c>ids</c> the v1 hashes: Transmission names every torrent by its v1 <c>hashString</c> (a hybrid's too) and finds
    /// one by it in any letter case, while a v2 hash names none of its torrents — so v2 hashes are left out, and a list of
    /// nothing but v2 hashes asks nothing. A hash Transmission doesn't have is no error: it lists none. Never the whole
    /// list: it would read every torrent to find one.
    /// </para>
    /// <para>
    /// A hash that isn't 40 or 64 ASCII hexadecimal characters is the caller's bug, thrown as
    /// <see cref="ArgumentException"/> before anything is sent. No hash at all asks nothing.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> FindJobsAsync(
        IntegrationConnectionSettings settings, IReadOnlyCollection<string> infoHashes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(infoHashes);
        var hashes = new List<string>();
        foreach (var hash in infoHashes)
        {
            if (LowerHex(hash, V1HashLength) is { } v1)
            {
                if (!hashes.Contains(v1, StringComparer.Ordinal))
                    hashes.Add(v1);
            }
            else if (LowerHex(hash, V2HashLength) is null)
            {
                throw new ArgumentException("Each info hash must be 40 or 64 hexadecimal characters.", nameof(infoHashes));
            }
        }

        if (hashes.Count == 0)
            return Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientJob>>.Succeeded([]));

        return CallAsync<IReadOnlyList<ClientJob>>(
            settings, (client, sessionSecrets, token) => ReadJobsAsync(client, settings, hashes, folder: null, sessionSecrets, token), cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>session-get</c> first, then one <c>torrent-get</c> of <see cref="JobFields"/> without <c>ids</c> — every torrent,
    /// as Transmission filters by no folder — keeping those whose <c>downloadDir</c> is the destination's folder
    /// (<see cref="NormalizedPath.IsSameAs"/>), never those that merely carry its name as a label (ADR 0039). A few hundred
    /// bytes a torrent, far below what the client reads.
    /// </para>
    /// <para>
    /// MAM-30 — only the overwrite check asks it of Transmission: a job here is named by its v1 hash for good, so the
    /// renamed-job search (D19) never runs for it. A key that isn't an absolute folder is the caller's bug, thrown as
    /// <see cref="ArgumentException"/> before anything is sent.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ListDestinationJobsAsync(
        IntegrationConnectionSettings settings, string destinationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("A destination key must not be blank.", nameof(destinationKey));
        if (!NormalizedPath.TryParse(destinationKey, out var folder, out _))
            throw new ArgumentException("A folder destination's key must be an absolute path.", nameof(destinationKey));

        return CallAsync<IReadOnlyList<ClientJob>>(
            settings, (client, sessionSecrets, token) => ReadJobsAsync(client, settings, hashes: null, folder, sessionSecrets, token), cancellationToken);
    }

    /// <summary>The torrents of <paramref name="hashes"/>, or every torrent when there are none, as jobs — only those in
    /// <paramref name="folder"/> when it is given.</summary>
    private async Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ReadJobsAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        IReadOnlyList<string>? hashes,
        NormalizedPath? folder,
        ICollection<string> sessionSecrets,
        CancellationToken cancellationToken)
    {
        var call = NewCall(settings, isTest: false, sessionSecrets);
        var (session, failure) = await OpenAsync(client, call, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientJob>>(failure);

        var arguments = new JsonObject { ["fields"] = Names(JobFields) };
        if (hashes is not null)
            arguments["ids"] = Names(hashes);
        var (answer, readFailure) = await SendRpcAsync(client, call, TorrentGetMethod, arguments, sessionSecrets, cancellationToken);
        if (readFailure is not null)
            return Failed<IReadOnlyList<ClientJob>>(readFailure);
        if (!answer!.IsSuccess)
            return Failed<IReadOnlyList<ClientJob>>(RequestRefused(answer, settings, sessionSecrets));
        if (TorrentsOf(answer.Arguments) is not { } torrents)
            return IntegrationCallResult<IReadOnlyList<ClientJob>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorrentsError);

        var secrets = CallSecrets(settings, sessionSecrets);
        var jobs = new List<ClientJob>();
        foreach (var torrent in torrents)
        {
            // A torrent without a usable hashString is left out: a job is followed by its id, and one without can't be.
            if (ReadJob(torrent, session!.NameReading, secrets) is not { } job)
                continue;
            if (folder is null || (NormalizedPath.TryParse(job.ServiceSavePath, out var saved, out _) && saved.IsSameAs(folder)))
                jobs.Add(job);
        }

        return IntegrationCallResult<IReadOnlyList<ClientJob>>.Succeeded(jobs);
    }

    /// <summary>The torrents of a <c>torrent-get</c> answer, or <c>null</c> when its <c>torrents</c> isn't a list of
    /// objects, which is how every version answers.</summary>
    private static List<JsonElement>? TorrentsOf(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("torrents", out var torrents) || torrents.ValueKind != JsonValueKind.Array)
            return null;

        var listed = new List<JsonElement>();
        foreach (var torrent in torrents.EnumerateArray())
        {
            if (torrent.ValueKind != JsonValueKind.Object)
                return null;
            listed.Add(torrent);
        }

        return listed;
    }

    /// <summary>
    /// One torrent as a job — or <c>null</c> when it has no usable <c>hashString</c>, for the caller to leave out or to
    /// fail the whole answer by (<see cref="ListJobsWithFilesAsync"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Complete is <c>metadataPercentComplete</c> 1 with <c>leftUntilDone</c> 0 (MAM-29): a magnet still fetching its
    /// metadata has nothing left at 0 %, and <c>isFinished</c> means a seeding limit reached, never complete. The progress
    /// is 1 then, else <c>percentDone</c> below 1, else unknown.
    /// </para>
    /// <para>
    /// The folder it saves to and the destination it is in are its <c>downloadDir</c>, exactly as sent. D20 — its content
    /// is that folder and its <c>name</c> cleaned the way this Transmission cleans names — the file of a torrent of one
    /// file, the folder of one of several — once it has its metadata; unknown before, or when the name cleans to nothing
    /// usable, which the overwrite check reads as "can't tell".
    /// </para>
    /// </remarks>
    private static ClientJob? ReadJob(JsonElement torrent, Func<string, IReadOnlyList<string>>? nameReading, string?[] secrets)
    {
        if (LowerHex(Text(torrent, "hashString"), V1HashLength) is not { } hash)
            return null;

        var hasMetadata = Fraction(torrent, "metadataPercentComplete") is >= 1;
        var complete = hasMetadata && Whole(torrent, "leftUntilDone") == 0;
        var name = Text(torrent, "name");
        // Exactly as sent: it names a folder. "" is no folder.
        var downloadDir = Text(torrent, "downloadDir") is { Length: > 0 } dir ? dir : null;
        return new ClientJob(
            hash,
            RemoteText.Clean(name, JobNameMaximumLength, secrets),
            StateOf(Whole(torrent, "status"), Whole(torrent, "error"), complete),
            Progress(torrent, complete),
            downloadDir,
            hasMetadata ? ContentPath(downloadDir, name, nameReading) : null,
            [hash],
            downloadDir);
    }

    /// <summary>1 when complete, else <c>percentDone</c> when it is from 0 to below 1, else unknown: a <c>percentDone</c> of 1
    /// with something left is a rounding, not a download done.</summary>
    private static double? Progress(JsonElement torrent, bool complete)
    {
        if (complete)
            return 1;
        return Fraction(torrent, "percentDone") is { } progress && progress is >= 0 and < 1 ? progress : null;
    }

    /// <summary>
    /// D10 — Transmission's status as the core's. Only a local error (<c>error</c> 3: a full disk, a permission, files
    /// gone) is an error: a tracker's warning or error (1, 2) leaves a download going, and calling it errored would hold
    /// a replacement for review. A stopped torrent is completed when complete and paused otherwise; a verify or a queue
    /// waits; a status this build doesn't know — or none — is never guessed into one it does.
    /// </summary>
    private static ClientJobState StateOf(long? status, long? error, bool complete) =>
        error == 3
            ? ClientJobState.Errored
            : status switch
            {
                0 => complete ? ClientJobState.Completed : ClientJobState.Paused,
                1 or 2 or 3 => ClientJobState.Queued,
                4 => ClientJobState.Downloading,
                5 or 6 => ClientJobState.Seeding,
                _ => ClientJobState.Unknown,
            };

    /// <summary>
    /// D20 — where a torrent's content is: its folder with its name cleaned by <paramref name="nameReading"/>, in the
    /// folder's canonical form — or <c>null</c> when the folder isn't a path, the reading is unknown, or the name cleans to
    /// nothing (then a torrent of several files has no folder of its own, which the name alone can't tell).
    /// </summary>
    private static string? ContentPath(string? downloadDir, string? name, Func<string, IReadOnlyList<string>>? nameReading)
    {
        if (nameReading is null || name is null || !NormalizedPath.TryParse(downloadDir, out var folder, out _))
            return null;

        var names = nameReading(name);
        return names.Count == 0 ? null : folder.Append(names).ToString();
    }
}
