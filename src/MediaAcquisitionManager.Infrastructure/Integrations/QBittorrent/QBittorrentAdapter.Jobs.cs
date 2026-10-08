using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

// MAM-13 — the download client contract's job lookup: the torrents qBittorrent lists under the info hashes
// the app worked out from a payload, read from GET api/v2/torrents/info?hashes=… as an allowlist. MAM-14 adds
// the torrents of one category, GET api/v2/torrents/info?category=… — or every torrent, kept to the category
// here, for a category whose name holds a '+' — read the same way. Every request is the sign-in or a GET.
internal sealed partial class QBittorrentAdapter
{
    private const string NotTorrentsError = "The service at this address answered, but not with qBittorrent's torrents.";

    /// <summary>Longer than any release name worth showing, short enough that a hostile name can't fill a
    /// job's row.</summary>
    private const int JobNameMaximumLength = 1000;

    /// <summary>A v1 info hash in hexadecimal.</summary>
    private const int V1HashLength = 40;

    /// <summary>A v2 info hash in hexadecimal.</summary>
    private const int V2HashLength = 64;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The session and <c>GET /api/v2/app/version</c> first, as the destinations read them
    /// (<see cref="OpenCheckedSessionAsync"/>), then <c>GET /api/v2/torrents/info?hashes=…</c>: qBittorrent
    /// names a torrent by its TorrentID — the v1 hash, or the v2 hash cut to 40 characters — and reads
    /// <c>hashes</c> as TorrentIDs joined by <c>|</c>, so each hash is sent that way, once. A hybrid torrent
    /// is named by its v2 hash where qBittorrent is built on libtorrent 2 (<c>info_hash_t::get_best</c>) and
    /// by its v1 hash on 1.2, so a caller that passes both finds it either way. Never the whole list: it
    /// would read every torrent of a client to find one.
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
        var torrentIds = TorrentIdsOf(infoHashes);
        if (torrentIds.Count == 0)
            return Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientJob>>.Succeeded([]));

        // Escaped whole, | included: a hash is hexadecimal, so nothing else in it needs escaping.
        var path = "api/v2/torrents/info?hashes=" + Uri.EscapeDataString(string.Join('|', torrentIds));
        return CallAsync<IReadOnlyList<ClientJob>>(
            settings, (client, sessionSecrets, token) => ReadJobsAsync(client, settings, path, sessionSecrets, category: null, token), cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The session and <c>GET /api/v2/app/version</c> first, then <c>GET /api/v2/torrents/info?category=…</c>,
    /// read as <see cref="FindJobsAsync"/> reads its answer. MAM-14 — this is how a job sent as a magnet naming
    /// only its v1 hash is found again once qBittorrent built on libtorrent 2 has renamed it to its v2 hash cut
    /// to 40 characters, which no stored hash names (ADR 0013): its category still lists it, with the v1 hash
    /// among its hashes.
    /// </para>
    /// <para>
    /// The key is sent exactly as given, never trimmed: qBittorrent compares a category by its whole name. A
    /// blank one is the caller's bug, thrown as <see cref="ArgumentException"/> before anything is sent —
    /// qBittorrent reads <c>category=</c> as "no category" and would list every torrent without one.
    /// </para>
    /// <para>
    /// A key holding a <c>+</c> can't be sent at all: qBittorrent's request parser
    /// (<c>RequestParser::parseRequestLine</c>, 4.4.1 to today) percent-decodes a query value and only then
    /// turns <c>+</c> into a space, so <c>%2B</c> arrives as a space too and <c>category=Movies%2BTV</c> asks
    /// for "Movies TV" — another category, or none, which would read as the job gone. Such a key asks
    /// <c>GET /api/v2/torrents/info</c> for every torrent instead and keeps those whose category is exactly
    /// the key, by ordinal comparison, read through the same allowlist — a bigger answer, about 2 KB a torrent,
    /// so a client past a couple of thousand outgrows
    /// <see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>: a failed call, which decides
    /// no job (ADR 0014). A category may hold a <c>+</c>
    /// (qBittorrent forbids only <c>\</c> and <c>//</c>), and a send reaches it, as <c>torrents/add</c>'s
    /// form body is decoded the right way round.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ListDestinationJobsAsync(
        IntegrationConnectionSettings settings, string destinationKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(destinationKey))
            throw new ArgumentException("A destination key must not be blank.", nameof(destinationKey));

        // No GET query carries a '+' to qBittorrent (see the remarks): such a key is kept to here instead.
        var keptHere = destinationKey.Contains('+', StringComparison.Ordinal);
        var path = keptHere ? "api/v2/torrents/info" : "api/v2/torrents/info?category=" + Uri.EscapeDataString(destinationKey);
        return CallAsync<IReadOnlyList<ClientJob>>(
            settings,
            (client, sessionSecrets, token) => ReadJobsAsync(client, settings, path, sessionSecrets, keptHere ? destinationKey : null, token),
            cancellationToken);
    }

    /// <summary>The torrents <paramref name="path"/> lists, as jobs — only those whose category is exactly
    /// <paramref name="category"/> when it is given.</summary>
    private async Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ReadJobsAsync(
        HttpClient client,
        IntegrationConnectionSettings settings,
        string path,
        ICollection<string> sessionSecrets,
        string? category,
        CancellationToken cancellationToken)
    {
        var (call, failure) = await OpenCheckedSessionAsync(client, settings, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientJob>>(failure);

        var (answer, readFailure) = await GetAsync(client, settings, call!, path, sessionSecrets, cancellationToken);
        if (readFailure is not null)
            return Failed<IReadOnlyList<ClientJob>>(readFailure);

        List<ClientJob>? jobs;
        using (answer)
        using (var document = await TryReadJsonAsync(answer!, cancellationToken))
        {
            jobs = document is null ? null : ReadJobs(document.RootElement, CallSecrets(settings, sessionSecrets));
        }

        if (jobs is null)
            return IntegrationCallResult<IReadOnlyList<ClientJob>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorrentsError);

        return IntegrationCallResult<IReadOnlyList<ClientJob>>.Succeeded(
            category is null ? jobs : [.. jobs.Where(job => string.Equals(job.DestinationKey, category, StringComparison.Ordinal))]);
    }

    /// <summary>
    /// The TorrentIDs of <paramref name="infoHashes"/>, lower-case and each once, in the order given; throws
    /// when one isn't an info hash.
    /// </summary>
    private static List<string> TorrentIdsOf(IReadOnlyCollection<string> infoHashes)
    {
        var torrentIds = new List<string>();
        foreach (var hash in infoHashes)
        {
            if ((LowerHex(hash, V1HashLength) ?? LowerHex(hash, V2HashLength)) is not { } lower)
                throw new ArgumentException("Each info hash must be 40 or 64 hexadecimal characters.", nameof(infoHashes));

            var torrentId = lower[..TorrentIdLength];
            if (!torrentIds.Contains(torrentId, StringComparer.Ordinal))
                torrentIds.Add(torrentId);
        }

        return torrentIds;
    }

    /// <summary>
    /// Every torrent of a <c>torrents/info</c> answer as a job — or <c>null</c> when the body is not a JSON
    /// array of objects, which is how every version answers.
    /// </summary>
    /// <remarks>
    /// Read as an allowlist, property by property: the id (<c>hash</c>), the hashes, the name, the state, the
    /// progress, the category, where the content is and (MAM-38) the folder it saves to. A torrent without a
    /// usable <c>hash</c> is left out rather than failing the rest: a job is followed by its id, and one
    /// without can't be. The trackers, the comment and the magnet link, which can carry the owner's passkey,
    /// go no further.
    /// </remarks>
    private static List<ClientJob>? ReadJobs(JsonElement root, string?[] secrets)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;

        var jobs = new List<ClientJob>();
        foreach (var torrent in root.EnumerateArray())
        {
            if (torrent.ValueKind != JsonValueKind.Object)
                return null;

            var hash = Text(torrent, "hash");
            if ((LowerHex(hash, V1HashLength) ?? LowerHex(hash, V2HashLength)) is not { } id)
                continue;

            // 5.2 lists a hash a torrent doesn't have as "" (infohash_v2 of a v1 torrent).
            var v1 = LowerHex(Text(torrent, "infohash_v1"), V1HashLength);
            var v2 = LowerHex(Text(torrent, "infohash_v2"), V2HashLength);
            jobs.Add(new ClientJob(
                id,
                RemoteText.Clean(Text(torrent, "name"), JobNameMaximumLength, secrets),
                StateOf(Text(torrent, "state")),
                Progress(torrent),
                // Exactly as qBittorrent names it, never trimmed: it is matched against a destination's key.
                // "" is qBittorrent's "no category".
                Text(torrent, "category") is { Length: > 0 } category ? category : null,
                // Exactly as sent: it names a folder or file. "" before a magnet's metadata has arrived.
                Text(torrent, "content_path") is { Length: > 0 } contentPath ? contentPath : null,
                [.. new[] { id, v1, v2 }.OfType<string>().Distinct(StringComparer.Ordinal)],
                // MAM-38 — exactly as sent, as the content path is: where the files end up, while content_path
                // follows an unfinished download into qBittorrent's folder for incomplete torrents.
                Text(torrent, "save_path") is { Length: > 0 } savePath ? savePath : null));
        }

        return jobs;
    }

    /// <summary>
    /// qBittorrent's state as the core's. Its own names, compared exactly: 5.0 renamed <c>pausedUP</c> and
    /// <c>pausedDL</c> to <c>stoppedUP</c> and <c>stoppedDL</c>, so both are read. A state this build doesn't
    /// know — <c>moving</c>, <c>unknown</c>, or one a later version adds — is never guessed into one it does.
    /// </summary>
    private static ClientJobState StateOf(string? state) => state switch
    {
        "error" or "missingFiles" => ClientJobState.Errored,
        "uploading" or "stalledUP" or "forcedUP" or "queuedUP" or "checkingUP" => ClientJobState.Seeding,
        "pausedUP" or "stoppedUP" => ClientJobState.Completed,
        "downloading" or "stalledDL" or "forcedDL" or "metaDL" or "forcedMetaDL" => ClientJobState.Downloading,
        "queuedDL" or "checkingDL" or "allocating" or "checkingResumeData" => ClientJobState.Queued,
        "pausedDL" or "stoppedDL" => ClientJobState.Paused,
        _ => ClientJobState.Unknown,
    };

    /// <summary>The <c>progress</c> as a number from 0 to 1, or <c>null</c> when it is anything else.</summary>
    private static double? Progress(JsonElement torrent) =>
        torrent.TryGetProperty("progress", out var progress)
        && progress.ValueKind == JsonValueKind.Number
        && progress.TryGetDouble(out var value)
        && value is >= 0 and <= 1
            ? value
            : null;

    /// <summary>The property as text, or <c>null</c> when it is missing or isn't text.</summary>
    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary><paramref name="value"/> lower-cased when it is <paramref name="length"/> ASCII hexadecimal
    /// characters — <see cref="char.IsAsciiHexDigit"/> takes no other script's digits — else <c>null</c>.</summary>
    private static string? LowerHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
}
