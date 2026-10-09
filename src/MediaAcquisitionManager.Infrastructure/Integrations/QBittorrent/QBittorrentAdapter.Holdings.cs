using System.Globalization;
using System.Text.Json;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Infrastructure.Integrations.QBittorrent;

// MAM-24 — the download client contract's whole listing: every torrent with its files, from one
// GET api/v2/torrents/info?includeFiles=true (qBittorrent 5.2 and later), read as an allowlist, for telling which
// torrent holds a library copy before the owner schedules its replacement (ADR 0031). Every request is the sign-in
// or a GET.
internal sealed partial class QBittorrentAdapter
{
    /// <summary>
    /// MAM-24 — the oldest qBittorrent whose <c>torrents/info</c> lists each torrent's files: <c>includeFiles</c> came
    /// with Web API 2.11.8, and 5.2.0 ships 2.15.1 (5.1.2 is 2.11.4, and neither it nor 5.1.4 reads the parameter, by
    /// qBittorrent's source). An older one ignores it without a word and lists every torrent without files, which
    /// would read as no library copy held — a false all-clear — so it is refused instead.
    /// </summary>
    private static readonly Version HoldingsMinimumVersion = new(5, 2, 0);

    /// <summary>
    /// MAM-24 — the answer outgrew <see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>: on a big
    /// enough installation that is the honest size of the list, not a wrong address, as the shared sentence would say.
    /// </summary>
    private static readonly string HoldingsTooLargeError = string.Create(
        CultureInfo.InvariantCulture,
        $"qBittorrent's list of every torrent with its files is larger than the app reads ({IntegrationsInfrastructureExtensions.MaximumResponseBytes / (1024 * 1024)} MiB), so the app couldn't tell which torrents hold these copies.");

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The session and <c>GET /api/v2/app/version</c> first (<see cref="OpenCheckedSessionAsync"/>), whose version
    /// also decides whether qBittorrent can list the files at all (<see cref="HoldingsMinimumVersion"/>): an older
    /// one is refused before anything else is asked. Then <c>GET /api/v2/torrents/info?includeFiles=true</c>, once,
    /// with no filter — every torrent in every state and category, since a file a torrent lists is held whatever the
    /// torrent's state (ADR 0031) — and never <c>includeTrackers</c>, whose addresses can carry the owner's passkey.
    /// </para>
    /// <para>
    /// Each torrent is read as <see cref="FindJobsAsync"/> reads one (<see cref="ReadJob"/>), and its <c>files</c> as
    /// <see cref="ListJobFilesAsync"/> reads them (<see cref="ReadFiles"/>): each path exactly as qBittorrent names it,
    /// relative to the save path. qBittorrent lists the files only of a torrent whose metadata it has, so one without
    /// <c>files</c> — a magnet still fetching its metadata — comes back with <see cref="ClientJobWithFiles.Files"/>
    /// <c>null</c>, never as a torrent with no files. Unlike the job lookup, a torrent without a usable <c>hash</c>, or
    /// a <c>files</c> value that isn't a list of files, fails the whole answer: left out, it could hide a held copy.
    /// </para>
    /// <para>
    /// The answer is about 2 KB a torrent plus about 300 bytes a file. One larger than the client buffers
    /// (<see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>) is a failed read in its own words
    /// (<see cref="HoldingsTooLargeError"/>), which the caller reports as copies it couldn't check.
    /// </para>
    /// </remarks>
    public Task<IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>> ListJobsWithFilesAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CallAsync<IReadOnlyList<ClientJobWithFiles>>(
            settings,
            (client, sessionSecrets, token) => ReadJobsWithFilesAsync(client, settings, sessionSecrets, token),
            cancellationToken,
            oversizeError: HoldingsTooLargeError);
    }

    private async Task<IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>> ReadJobsWithFilesAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var (call, reported, version, failure) = await OpenCheckedSessionAsync(client, settings, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientJobWithFiles>>(failure);
        if (version! < HoldingsMinimumVersion)
        {
            return IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Failed(
                IntegrationCallStatus.UnsupportedVersion,
                $"This is qBittorrent {reported}; telling which torrents hold a library file needs qBittorrent {HoldingsMinimumVersion.ToString(2)} or later.");
        }

        var (answer, readFailure) = await GetAsync(client, settings, call!, "api/v2/torrents/info?includeFiles=true", sessionSecrets, cancellationToken);
        if (readFailure is not null)
            return Failed<IReadOnlyList<ClientJobWithFiles>>(readFailure);

        (List<ClientJobWithFiles>? Jobs, string? Error) read;
        using (answer)
        using (var document = await TryReadJsonAsync(answer!, cancellationToken))
        {
            read = document is null ? (null, NotTorrentsError) : ReadJobsWithFiles(document.RootElement, CallSecrets(settings, sessionSecrets));
        }

        return read.Jobs is { } jobs
            ? IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Succeeded(jobs)
            : IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Failed(IntegrationCallStatus.UnexpectedResponse, read.Error!);
    }

    /// <summary>
    /// Every torrent of a <c>torrents/info?includeFiles=true</c> answer with its files — or the sentence the whole
    /// answer fails with: <see cref="NotTorrentsError"/> when it isn't a JSON array of objects each with a usable
    /// <c>hash</c>, <see cref="NotFilesError"/> when a torrent's <c>files</c> isn't one of files each with a usable
    /// <c>name</c>.
    /// </summary>
    private static (List<ClientJobWithFiles>? Jobs, string? Error) ReadJobsWithFiles(JsonElement root, string?[] secrets)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return (null, NotTorrentsError);

        var jobs = new List<ClientJobWithFiles>();
        foreach (var torrent in root.EnumerateArray())
        {
            // Never left out, as the job lookup leaves one out: a torrent missing from this list is a hole a held
            // copy could hide in.
            if (torrent.ValueKind != JsonValueKind.Object || ReadJob(torrent, secrets) is not { } job)
                return (null, NotTorrentsError);

            // No "files" at all is qBittorrent's torrent without metadata; a "files" that is present but not a list
            // of files — null included — is not an answer of qBittorrent's.
            if (!torrent.TryGetProperty("files", out var files))
            {
                jobs.Add(new ClientJobWithFiles(job, null));
                continue;
            }

            if (ReadFiles(files) is not { } read)
                return (null, NotFilesError);
            jobs.Add(new ClientJobWithFiles(job, read));
        }

        return (jobs, null);
    }
}
