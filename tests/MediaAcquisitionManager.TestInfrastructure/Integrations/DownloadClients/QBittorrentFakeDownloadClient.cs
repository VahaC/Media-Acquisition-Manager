using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

/// <summary>
/// MAM-30 — <see cref="IFakeDownloadClient"/> over a <see cref="FakeQBittorrentServer"/>: a destination is a category saving
/// to its folder, a job a torrent in that category, and a forgotten session the 403 qBittorrent answers for one it no longer
/// knows.
/// </summary>
public sealed class QBittorrentFakeDownloadClient : IFakeDownloadClient
{
    private const string AddPath = "/api/v2/torrents/add";
    private const string SignInPath = "/api/v2/auth/login";

    private readonly FakeQBittorrentServer _server = new();
    private readonly Dictionary<string, string> _folders = new(StringComparer.Ordinal);
    private RecordingHttpHandler? _handler;

    /// <inheritdoc/>
    public string TypeId => "qbittorrent";

    /// <inheritdoc/>
    public string DisplayName => "qBittorrent";

    /// <inheritdoc/>
    public string BaseUrl => _server.BaseUrl;

    /// <inheritdoc/>
    public string Password => FakeQBittorrentServer.DefaultPassword;

    /// <inheritdoc/>
    public IntegrationConnectionSettings Settings { get; } = new(
        Guid.Parse("7a1c5e3b-2d4f-4b6a-8c9e-0f1a2b3c4d5e"),
        FakeQBittorrentServer.DefaultBaseUrl,
        FakeQBittorrentServer.DefaultUserName,
        FakeQBittorrentServer.DefaultPassword,
        null);

    /// <inheritdoc/>
    public string AddWrite => $"POST {AddPath}";

    /// <inheritdoc/>
    /// <remarks>Every request but a GET and the sign-in: the fake answers anything else 405, and records it all the same.</remarks>
    public IReadOnlyList<string> Writes =>
        [
            .. Handler.Requests
                .Where(request => request.Method != HttpMethod.Get && request.Uri.AbsolutePath != SignInPath)
                .Select(request => $"{request.Method} {request.Uri.AbsolutePath}"),
        ];

    /// <inheritdoc/>
    public IReadOnlyList<RecordedHttpRequest> Requests => Handler.Requests;

    /// <inheritdoc/>
    public int AddsSent => Handler.Requests.Count(request => request.Method == HttpMethod.Post && request.Uri.AbsolutePath == AddPath);

    /// <inheritdoc/>
    public int TorrentCount => _server.Torrents.Count;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ProposedFolders => [.. _folders.Values];

    private RecordingHttpHandler Handler => _handler ?? throw new InvalidOperationException("Arm the client on a harness first.");

    /// <inheritdoc/>
    public void Arm(IntegrationAdapterHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        _handler = harness.QBittorrentHttp;
        _server.Arm(_handler);
    }

    /// <inheritdoc/>
    public string Destination(string name, string folder)
    {
        _server.AddCategory(name, folder);
        _folders[name] = folder;
        return name;
    }

    /// <inheritdoc/>
    public DownloadSubmission Submission(ReleasePayload payload, string destinationKey) => new(payload, destinationKey, null);

    /// <inheritdoc/>
    public string Plant(TorrentInfoHashes hashes, string name, string destinationKey, FakeJobState state, IReadOnlyList<FakeJobFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var complete = state is FakeJobState.Seeding or FakeJobState.CompletedStopped;
        var hasMetadata = state is not (FakeJobState.FetchingMetadata or FakeJobState.StoppedWithoutMetadata);
        var size = files.Sum(file => file.Size);
        // Without metadata qBittorrent knows no size, and lists 0 %.
        var progress = complete ? 1 : !hasMetadata ? 0 : size == 0 ? 0.5 : (double)files.Sum(file => file.BytesDone) / size;
        var nativeState = state switch
        {
            FakeJobState.Queued => "queuedDL",
            FakeJobState.Checking => "checkingDL",
            FakeJobState.Downloading => "downloading",
            FakeJobState.FetchingMetadata => "metaDL",
            FakeJobState.Paused or FakeJobState.StoppedWithoutMetadata => "stoppedDL",
            FakeJobState.Seeding => "stalledUP",
            FakeJobState.CompletedStopped => "stoppedUP",
            _ => "error",
        };
        var torrent = FakeQBittorrentTorrent.Of(hashes, name, destinationKey, nativeState, progress, _folders[destinationKey]) with
        {
            HasMetadata = hasMetadata,
            Files = [.. files.Select(file => new FakeQBittorrentFile(file.RelativePath, file.Size, file.Size == 0 ? 1 : (double)file.BytesDone / file.Size, file.Wanted ? 1 : 0))],
        };
        _server.AddTorrent(torrent);
        return torrent.Hash;
    }

    /// <inheritdoc/>
    public void Remove(string clientJobId) => _server.RemoveTorrent(clientJobId);

    /// <inheritdoc/>
    public void MakeUnreachable() => _server.Unreachable = true;

    /// <inheritdoc/>
    public void RefuseCredentials() => _server.Password = "changed-in-qbittorrent";

    /// <inheritdoc/>
    public void ForgetSessions() => _server.ExpireSessions();

    /// <inheritdoc/>
    public void ForgetSessionsBeforeTheNextAdd() =>
        _server.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath == AddPath)
            {
                _server.Intercept = null;
                _server.ExpireSessions();
            }

            return null;
        };

    /// <inheritdoc/>
    public void StallAdds(Action? arrived = null) =>
        _server.Intercept = request =>
        {
            if (request.RequestUri!.AbsolutePath != AddPath)
                return null;
            arrived?.Invoke();
            return VendorResponses.Stalled();
        };

    /// <inheritdoc/>
    public void StallEverything(Action? arrived = null) =>
        _server.Intercept = _ =>
        {
            arrived?.Invoke();
            return VendorResponses.Stalled();
        };
}
