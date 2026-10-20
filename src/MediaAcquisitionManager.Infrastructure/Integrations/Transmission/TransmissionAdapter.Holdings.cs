using System.Text.Json.Nodes;
using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

// MAM-30 — the download client contract's whole listing: every torrent with its files, from one torrent-get, read as an
// allowlist, for telling which torrent holds a library copy before a replacement is scheduled and right before an old
// copy moves into the quarantine (ADR 0031). Every request is session-get or torrent-get: nothing here moves, relocates
// or renames a torrent or its files (torrent-set-location, torrent-rename-path) — a torrent's files stay where it put them.
internal sealed partial class TransmissionAdapter
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>session-get</c> first (<see cref="OpenAsync"/>), then one <c>torrent-get</c> without <c>ids</c> — every torrent
    /// in every state and folder, as a file a torrent lists is held whatever its state (ADR 0031) — of
    /// <see cref="JobFields"/> with <c>files</c> and <c>fileStats</c>. Every Transmission from 4.0 on lists every torrent's
    /// files this way, so no version gate beyond the session's.
    /// </para>
    /// <para>
    /// Each torrent is read as <see cref="FindJobsAsync"/> reads one (<see cref="ReadJob"/>), and its files as
    /// <see cref="ListJobFilesAsync"/> reads them (<see cref="ReadFiles"/>). A torrent without its metadata lists no files —
    /// a magnet still fetching it — and comes back with <see cref="ClientJobWithFiles.Files"/> <c>null</c>, never as a
    /// torrent with no files. Whole or nothing: a torrent without a usable <c>hashString</c>, files that can't be read, or a
    /// torrent with its metadata and no files at all fails the whole answer — left out, it could hide a held copy.
    /// </para>
    /// <para>
    /// The answer is a few hundred bytes a torrent and about 200 a file. One larger than the client buffers
    /// (<see cref="IntegrationsInfrastructureExtensions.MaximumResponseBytes"/>) is a failed read in its own words
    /// (<see cref="IntegrationAdapterMessages.HoldingsTooLarge"/>), which the caller reports as copies it couldn't check: on
    /// a big enough installation that is the honest size of the list, not a wrong address, as the shared sentence would say.
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
            oversizeError: IntegrationAdapterMessages.HoldingsTooLarge(Descriptor.DisplayName));
    }

    private async Task<IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>> ReadJobsWithFilesAsync(
        HttpClient client, IntegrationConnectionSettings settings, ICollection<string> sessionSecrets, CancellationToken cancellationToken)
    {
        var call = NewCall(settings, isTest: false, sessionSecrets);
        var (session, failure) = await OpenAsync(client, call, sessionSecrets, cancellationToken);
        if (failure is not null)
            return Failed<IReadOnlyList<ClientJobWithFiles>>(failure);

        var arguments = new JsonObject { ["fields"] = Names([.. JobFields, "files", "fileStats"]) };
        var (answer, readFailure) = await SendRpcAsync(client, call, TorrentGetMethod, arguments, sessionSecrets, cancellationToken);
        if (readFailure is not null)
            return Failed<IReadOnlyList<ClientJobWithFiles>>(readFailure);
        if (!answer!.IsSuccess)
            return Failed<IReadOnlyList<ClientJobWithFiles>>(RequestRefused(answer, settings, sessionSecrets));
        if (TorrentsOf(answer.Arguments) is not { } torrents)
            return IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorrentsError);

        var secrets = CallSecrets(settings, sessionSecrets);
        var jobs = new List<ClientJobWithFiles>();
        foreach (var torrent in torrents)
        {
            // Never left out, as the job lookup leaves one out: a torrent missing from this list is a hole a held copy
            // could hide in.
            if (ReadJob(torrent, session!.NameReading, secrets) is not { } job)
                return IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotTorrentsError);

            // Transmission lists files: [] for a torrent without its metadata: unknown, never none.
            if (Fraction(torrent, "metadataPercentComplete") is not >= 1)
            {
                jobs.Add(new ClientJobWithFiles(job, null));
                continue;
            }

            if (ReadFiles(torrent) is not { Count: > 0 } files)
                return IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Failed(IntegrationCallStatus.UnexpectedResponse, NotFilesError);
            jobs.Add(new ClientJobWithFiles(job, files));
        }

        return IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Succeeded(jobs);
    }
}
