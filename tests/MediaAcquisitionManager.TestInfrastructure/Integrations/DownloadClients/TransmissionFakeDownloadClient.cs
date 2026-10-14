using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

/// <summary>
/// MAM-30 — <see cref="IFakeDownloadClient"/> over a <see cref="FakeTransmissionServer"/>: a destination is a folder the
/// server sees, a submission carries the destination's name as the torrent's label, a job is a torrent in that folder, and a
/// forgotten session the 409 Transmission answers for an id it rotated away.
/// </summary>
public sealed class TransmissionFakeDownloadClient : IFakeDownloadClient
{
    private const string AddMethod = "torrent-add";

    private readonly FakeTransmissionServer _server = new();
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private RecordingHttpHandler? _handler;

    /// <inheritdoc/>
    public string TypeId => "transmission";

    /// <inheritdoc/>
    public string DisplayName => "Transmission";

    /// <inheritdoc/>
    public string BaseUrl => _server.BaseUrl;

    /// <inheritdoc/>
    public string Password => FakeTransmissionServer.DefaultPassword;

    /// <inheritdoc/>
    public IntegrationConnectionSettings Settings { get; } = new(
        Guid.Parse("7a1c5e3b-2d4f-4b6a-8c9e-0f1a2b3c4d5e"),
        FakeTransmissionServer.DefaultBaseUrl,
        FakeTransmissionServer.DefaultUserName,
        FakeTransmissionServer.DefaultPassword,
        null);

    /// <inheritdoc/>
    public string AddWrite => AddMethod;

    /// <inheritdoc/>
    public IReadOnlyList<string> Writes => [.. _server.Writes.Select(call => call.Method)];

    /// <inheritdoc/>
    public IReadOnlyList<RecordedHttpRequest> Requests =>
        (_handler ?? throw new InvalidOperationException("Arm the client on a harness first.")).Requests;

    /// <inheritdoc/>
    public int AddsSent => Requests.Count(request => FakeTransmissionServer.Describe(request) == AddMethod);

    /// <inheritdoc/>
    public int TorrentCount => _server.Torrents.Count;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> ProposedFolders => [.. _names.Keys];

    /// <inheritdoc/>
    public void Arm(IntegrationAdapterHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        _handler = harness.TransmissionHttp;
        _server.Arm(_handler);
    }

    /// <inheritdoc/>
    public string Destination(string name, string folder)
    {
        _server.AddFolder(folder);
        _names[folder] = name;
        return folder;
    }

    /// <inheritdoc/>
    public DownloadSubmission Submission(ReleasePayload payload, string destinationKey) => new(payload, destinationKey, null, _names[destinationKey]);

    /// <inheritdoc/>
    public string Plant(TorrentInfoHashes hashes, string name, string destinationKey, FakeJobState state, IReadOnlyList<FakeJobFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var complete = state is FakeJobState.Seeding or FakeJobState.CompletedStopped;
        var torrent = FakeTransmissionTorrent.Of(
            hashes,
            name,
            destinationKey,
            [.. files.Select(file => new FakeTransmissionFile(file.RelativePath, file.Size, complete ? file.Size : file.BytesDone, file.Wanted))]);
        // A torrent not complete has something left, files or none.
        if (!complete && torrent.LeftUntilDone == 0)
            torrent = torrent with { SizeWhenDone = 2_000, LeftUntilDone = 1_000, PercentDone = 0.5 };
        torrent = state switch
        {
            FakeJobState.Queued => torrent with { Status = 3 },
            FakeJobState.Checking => torrent with { Status = 2 },
            FakeJobState.Downloading => torrent with { Status = 4 },
            // Without metadata Transmission knows no size, so it lists nothing left at 0 % (MAM-29).
            FakeJobState.FetchingMetadata => WithoutMetadata(torrent) with { Status = 4 },
            FakeJobState.StoppedWithoutMetadata => WithoutMetadata(torrent) with { Status = 0 },
            FakeJobState.Paused => torrent with { Status = 0 },
            FakeJobState.Seeding => torrent with { Status = 6 },
            FakeJobState.CompletedStopped => torrent with { Status = 0 },
            _ => torrent with { Status = 4, Error = 3, ErrorString = "No space left on device" },
        };
        return _server.AddTorrent(torrent).HashString;
    }

    /// <inheritdoc/>
    public void Remove(string clientJobId) => _server.RemoveTorrent(clientJobId);

    /// <inheritdoc/>
    public void MakeUnreachable() => _server.Unreachable = true;

    /// <inheritdoc/>
    public void RefuseCredentials() => _server.Password = "changed-in-transmission";

    /// <inheritdoc/>
    public void ForgetSessions() => _server.RotateSession();

    /// <inheritdoc/>
    public void ForgetSessionsBeforeTheNextAdd() =>
        _server.Intercept = (_, call) =>
        {
            if (call?.Method == AddMethod)
            {
                _server.Intercept = null;
                _server.RotateSession();
            }

            return null;
        };

    /// <inheritdoc/>
    public void StallAdds(Action? arrived = null) =>
        _server.Intercept = (_, call) =>
        {
            if (call?.Method != AddMethod)
                return null;
            arrived?.Invoke();
            return VendorResponses.Stalled();
        };

    /// <inheritdoc/>
    public void StallEverything(Action? arrived = null) =>
        _server.Intercept = (_, _) =>
        {
            arrived?.Invoke();
            return VendorResponses.Stalled();
        };

    private static FakeTransmissionTorrent WithoutMetadata(FakeTransmissionTorrent torrent) =>
        torrent with { MetadataPercentComplete = 0, Files = [], PercentDone = 0, LeftUntilDone = 0, SizeWhenDone = 0, TotalSize = 0 };
}
