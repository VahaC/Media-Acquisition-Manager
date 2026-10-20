using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Integrations;
using MediaAcquisitionManager.Infrastructure.Integrations.Transmission;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-30 — one more download client type, registered in the test host by <see cref="Http.FolderClientApiFactory"/>,
/// whose destinations are folders (ADR 0039): the app's side of a Transmission — the folders the app proposes, the send
/// with its label, the overwrite check, the holdings, the following — tested over HTTP without one. It has
/// Transmission's capabilities and answers the way its adapter does: its own download folder and each folder proposed,
/// merged when they are one folder, each it can't see with its sentence; a proposed or sent folder that isn't a full path
/// throws, as the adapter's does.
/// </summary>
/// <remarks>
/// <para>
/// Unarmed it answers every call <see cref="IntegrationCallStatus.Unreachable"/>, so a test that forgot to arm it sees a
/// failing client and can't pass for a working one. <see cref="Arm"/> makes it answer from what the test planted: its
/// download folder, the folders it can't see, its torrents.
/// </para>
/// <para>
/// Thread safe: the Destinations page asks several clients at once. Every call is recorded (<see cref="Calls"/>), with what
/// the app proposed (<see cref="Proposals"/>) and what it sent (<see cref="Submissions"/>).
/// </para>
/// <para>
/// MAM-47 (ADR 0042) — where it answers as Transmission's adapter would, it says so in the adapters' own sentences: a
/// folder it can't see (<see cref="TransmissionAdapter.FolderNotSeen"/>) and a torrent it no longer has
/// (<see cref="IntegrationAdapterMessages.TorrentGone"/>), each naming the fake, so a test reads the very keys a
/// reader of Transmission's sees and the English of <see cref="CantSeeProblem"/>. Its own words (its connection test's,
/// <see cref="UnarmedMessage"/>) are no sentence of the app's and travel as <see cref="Message.Legacy"/>.
/// </para>
/// </remarks>
public sealed class FakeFolderDownloadClient : IIntegrationAdapter, IDownloadClient
{
    /// <summary>The type id integrations of this fake client are stored with.</summary>
    public const string TypeId = "fake-folder-client";

    /// <summary>The product name the fake describes itself with.</summary>
    public const string DisplayName = "Fake folder client";

    /// <summary>What an unarmed fake answers every call with.</summary>
    public const string UnarmedMessage = "The fake folder client was not armed.";

    /// <summary>Its own download folder unless a test arms another: the linuxserver image's.</summary>
    public const string DefaultDownloadFolder = "/downloads/complete";

    /// <summary>Why it can't see a folder, in the words Transmission's <c>free-space</c> answers a missing one with.</summary>
    private const string CantSeeReason = "No such file or directory";

    private readonly object _gate = new();
    private readonly List<string> _calls = [];
    private readonly List<IReadOnlyList<string>> _proposals = [];
    private readonly List<DownloadSubmission> _submissions = [];
    private readonly List<NormalizedPath> _unseen = [];
    private readonly List<FakeFolderTorrent> _torrents = [];
    private bool _armed;
    private string? _downloadFolder;

    public IntegrationAdapterDescriptor Descriptor { get; } = new(
        TypeId,
        DisplayName,
        IntegrationKind.DownloadClient,
        UserName: CredentialRequirement.Optional,
        Password: CredentialRequirement.Optional,
        ApiKey: CredentialRequirement.NotUsed,
        ReportsFilePaths: true);

    /// <summary>Transmission's: folders, its own layout, <c>.part</c>, no torrent that is v2 only, ids that never change.</summary>
    public DownloadClientCapabilities Capabilities { get; } = new(
        [ReleasePayloadKind.TorrentFile, ReleasePayloadKind.Magnet],
        DestinationsHavePaths: true,
        SubmissionReturnsJobId: true,
        DownloadDestinationKind.Folder,
        TorrentFilePlacement.OwnLayoutOnly,
        PartialFileSuffix: ".part",
        TakesV2OnlyTorrents: false,
        JobIdsMayChange: false);

    /// <summary>When set, the destination listing answers this failure instead.</summary>
    public IntegrationCallResult<IReadOnlyList<ClientDestination>>? ListingFailure { get; set; }

    /// <summary>When set, a submission answers this failure instead of taking the torrent.</summary>
    public IntegrationCallResult<SubmissionReceipt>? SubmissionFailure { get; set; }

    /// <summary>When set, the listing of a destination's torrents answers this failure.</summary>
    public IntegrationCallResult<IReadOnlyList<ClientJob>>? DestinationJobsFailure { get; set; }

    /// <summary>Every call since the last <see cref="Reset"/>, oldest first: <c>ListDestinations</c>, <c>Submit</c>,
    /// <c>FindJobs</c>, <c>ListJobFiles {id}</c>, <c>ListDestinationJobs {folder}</c>, <c>ListJobsWithFiles</c>,
    /// <c>TestConnection</c>.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    /// <summary>The folders each destination listing was proposed, in the order given.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Proposals
    {
        get
        {
            lock (_gate)
                return [.. _proposals];
        }
    }

    /// <summary>Every submission it was handed, taken or not.</summary>
    public IReadOnlyList<DownloadSubmission> Submissions
    {
        get
        {
            lock (_gate)
                return [.. _submissions];
        }
    }

    /// <summary>The sentence it lists a folder it can't see with — Transmission's, naming the fake — in English.</summary>
    public static string CantSeeProblem(string folder) =>
        $"{DisplayName} can't see “{folder}” ({CantSeeReason}). Mount that folder into {DisplayName}, or add a path mapping on this integration that leads there, then look again.";

    /// <summary>Makes it answer, with <paramref name="downloadFolder"/> as its own download folder — none listed when
    /// <c>null</c>.</summary>
    public void Arm(string? downloadFolder = DefaultDownloadFolder)
    {
        lock (_gate)
        {
            _armed = true;
            _downloadFolder = downloadFolder;
        }
    }

    /// <summary>Makes it unable to see <paramref name="folder"/>, in any spelling of that folder.</summary>
    public void CantSee(string folder)
    {
        lock (_gate)
            _unseen.Add(Parse(folder));
    }

    /// <summary>Gives it <paramref name="torrent"/>.</summary>
    public void AddTorrent(FakeFolderTorrent torrent)
    {
        lock (_gate)
            _torrents.Add(torrent);
    }

    /// <summary>Disarms it and forgets everything planted and recorded.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _armed = false;
            _downloadFolder = null;
            _calls.Clear();
            _proposals.Clear();
            _submissions.Clear();
            _unseen.Clear();
            _torrents.Clear();
            ListingFailure = null;
            SubmissionFailure = null;
            DestinationJobsFailure = null;
        }
    }

    public Task<ConnectionTestResult> TestConnectionAsync(IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add("TestConnection");
            return Task.FromResult(_armed
                ? ConnectionTestResult.Succeeded(Message.Legacy($"Connected to {DisplayName}."))
                : ConnectionTestResult.Failed(IntegrationCallStatus.Unreachable, Message.Legacy(UnarmedMessage)));
        }
    }

    public Task<IntegrationCallResult<IReadOnlyList<ClientDestination>>> ListDestinationsAsync(
        IntegrationConnectionSettings settings, IReadOnlyCollection<string> proposedFolders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposedFolders);
        lock (_gate)
        {
            _calls.Add("ListDestinations");
            _proposals.Add([.. proposedFolders]);
            if (!_armed)
                return Task.FromResult(Unarmed<IReadOnlyList<ClientDestination>>());
            if (ListingFailure is { } failure)
                return Task.FromResult(failure);

            // As Transmission's adapter: a proposed folder that isn't a full path is a bug of the caller's, refused before
            // anything is asked.
            var proposed = proposedFolders.Select(Parse).ToList();
            var listed = new List<ClientDestination>();
            var downloadFolder = _downloadFolder is null ? null : Parse(_downloadFolder);
            if (downloadFolder is not null)
                listed.Add(Entry(downloadFolder, isDownloadFolder: true));
            foreach (var folder in proposed.Where(folder => !listed.Any(entry => Parse(entry.Key).IsSameAs(folder))))
                listed.Add(Entry(folder, isDownloadFolder: false));
            return Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientDestination>>.Succeeded(listed));
        }
    }

    public Task<IntegrationCallResult<SubmissionReceipt>> SubmitAsync(
        IntegrationConnectionSettings settings, DownloadSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        lock (_gate)
        {
            _calls.Add("Submit");
            _submissions.Add(submission);
            if (!_armed)
                return Task.FromResult(Unarmed<SubmissionReceipt>());

            // As Transmission's adapter validates before it sends anything.
            var folder = Parse(submission.DestinationKey);
            if (string.IsNullOrWhiteSpace(submission.Label) || submission.Label.Contains(',', StringComparison.Ordinal))
                throw new ArgumentException("A label must be given and can't contain a comma.", nameof(submission));
            if (SubmissionFailure is { } failure)
                return Task.FromResult(failure);

            var hash = (submission.ExpectedInfoHash ?? Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(40, '0')).ToLowerInvariant();
            _torrents.Add(new FakeFolderTorrent(hash, "Sent", folder.ToString(), null, ClientJobState.Queued, 0));
            return Task.FromResult(IntegrationCallResult<SubmissionReceipt>.Succeeded(new SubmissionReceipt(hash)));
        }
    }

    public Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> FindJobsAsync(
        IntegrationConnectionSettings settings, IReadOnlyCollection<string> infoHashes, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add("FindJobs");
            if (!_armed)
                return Task.FromResult(Unarmed<IReadOnlyList<ClientJob>>());
            return Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientJob>>.Succeeded(
            [
                .. _torrents
                    .Where(torrent => infoHashes.Contains(torrent.Hash, StringComparer.OrdinalIgnoreCase))
                    .Select(torrent => torrent.ToJob()),
            ]));
        }
    }

    public Task<IntegrationCallResult<IReadOnlyList<ClientJobFile>>> ListJobFilesAsync(
        IntegrationConnectionSettings settings, string clientJobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add($"ListJobFiles {clientJobId}");
            if (!_armed)
                return Task.FromResult(Unarmed<IReadOnlyList<ClientJobFile>>());
            return Task.FromResult(_torrents.FirstOrDefault(torrent => string.Equals(torrent.Hash, clientJobId, StringComparison.OrdinalIgnoreCase)) is { } found
                ? IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Succeeded(found.Files ?? [])
                : IntegrationCallResult<IReadOnlyList<ClientJobFile>>.Failed(IntegrationCallStatus.NotFound, IntegrationAdapterMessages.TorrentGone(DisplayName)));
        }
    }

    public Task<IntegrationCallResult<IReadOnlyList<ClientJob>>> ListDestinationJobsAsync(
        IntegrationConnectionSettings settings, string destinationKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add($"ListDestinationJobs {destinationKey}");
            if (!_armed)
                return Task.FromResult(Unarmed<IReadOnlyList<ClientJob>>());
            if (DestinationJobsFailure is { } failure)
                return Task.FromResult(failure);

            // Every torrent saving in that folder, whatever its label, as Transmission's adapter filters them.
            var folder = Parse(destinationKey);
            return Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientJob>>.Succeeded(
            [
                .. _torrents
                    .Where(torrent => NormalizedPath.TryParse(torrent.DownloadDir, out var saves, out _) && saves.IsSameAs(folder))
                    .Select(torrent => torrent.ToJob()),
            ]));
        }
    }

    public Task<IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>> ListJobsWithFilesAsync(
        IntegrationConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _calls.Add("ListJobsWithFiles");
            if (!_armed)
                return Task.FromResult(Unarmed<IReadOnlyList<ClientJobWithFiles>>());
            return Task.FromResult(IntegrationCallResult<IReadOnlyList<ClientJobWithFiles>>.Succeeded(
                [.. _torrents.Select(torrent => new ClientJobWithFiles(torrent.ToJob(), torrent.Files))]));
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private ClientDestination Entry(NormalizedPath folder, bool isDownloadFolder)
    {
        var key = folder.ToString();
        var problem = _unseen.Any(unseen => unseen.IsSameAs(folder))
            ? TransmissionAdapter.FolderNotSeen(DisplayName, key, VendorText.Clean(CantSeeReason, CantSeeReason.Length))
            : null;
        return new ClientDestination(key, key, key, isDownloadFolder, problem);
    }

    private static NormalizedPath Parse(string folder) =>
        NormalizedPath.TryParse(folder, out var path, out _)
            ? path
            : throw new ArgumentException($"“{folder}” isn't a full path, which the client would refuse.", nameof(folder));

    private static IntegrationCallResult<T> Unarmed<T>() where T : class =>
        IntegrationCallResult<T>.Failed(IntegrationCallStatus.Unreachable, Message.Legacy(UnarmedMessage));
}
