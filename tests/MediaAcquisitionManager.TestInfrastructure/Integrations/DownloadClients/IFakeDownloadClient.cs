using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.TestInfrastructure.Http;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations.DownloadClients;

/// <summary>
/// MAM-30 — one fake download client behind the real adapter of its type, driven in no client's words, so one contract
/// suite (<c>DownloadClientContractTests</c>) proves qBittorrent's adapter and Transmission's answer the core's
/// <see cref="IDownloadClient"/> alike: a destination is a category or a folder, a state is planted as the client shows it,
/// and a session is forgotten the way the client forgets it.
/// </summary>
/// <remarks>
/// A driver (Strategy over the two fakes): what differs between the clients lives here, so a test of the contract says
/// nothing about either. A test arms it on one <see cref="IntegrationAdapterHarness"/> before anything else.
/// </remarks>
public interface IFakeDownloadClient
{
    /// <summary>The adapter's type id: <c>qbittorrent</c> or <c>transmission</c>.</summary>
    string TypeId { get; }

    /// <summary>The adapter's display name, which its sentences name.</summary>
    string DisplayName { get; }

    /// <summary>The address the client answers at.</summary>
    string BaseUrl { get; }

    /// <summary>The password the settings carry: every message must mask it.</summary>
    string Password { get; }

    /// <summary>A saved integration of this client, with the credentials it takes.</summary>
    IntegrationConnectionSettings Settings { get; }

    /// <summary>What the client writes on the wire for the one write the app sends: its add.</summary>
    string AddWrite { get; }

    /// <summary>Every request the client read that could change it, as <see cref="AddWrite"/> names an add, oldest first.</summary>
    IReadOnlyList<string> Writes { get; }

    /// <summary>Every request the adapter sent the client, oldest first, whatever it answered.</summary>
    IReadOnlyList<RecordedHttpRequest> Requests { get; }

    /// <summary>How many adds the adapter sent, whether the client read them or not (a stalled one, say) — unlike
    /// <see cref="Writes"/>, which holds only what the client read.</summary>
    int AddsSent { get; }

    /// <summary>How many torrents the client has.</summary>
    int TorrentCount { get; }

    /// <summary>Has <paramref name="harness"/>'s network answer as this client.</summary>
    void Arm(IntegrationAdapterHarness harness);

    /// <summary>
    /// Makes a destination named <paramref name="name"/> whose jobs save to <paramref name="folder"/> — a category saving
    /// there, or the folder itself, which the client then sees — and answers its key.
    /// </summary>
    string Destination(string name, string folder);

    /// <summary>The folders a listing proposes to a client whose destinations are folders: every one made so far.</summary>
    IReadOnlyCollection<string> ProposedFolders { get; }

    /// <summary>A submission of <paramref name="payload"/> to the destination of <paramref name="destinationKey"/>, as the
    /// sender builds it for this client.</summary>
    DownloadSubmission Submission(ReleasePayload payload, string destinationKey);

    /// <summary>
    /// Gives the client a job named by <paramref name="hashes"/>, added in the client itself, in the destination of
    /// <paramref name="destinationKey"/> and in <paramref name="state"/>, with <paramref name="files"/>; answers the job's
    /// id as the client names it.
    /// </summary>
    string Plant(TorrentInfoHashes hashes, string name, string destinationKey, FakeJobState state, IReadOnlyList<FakeJobFile> files);

    /// <summary>Takes away the job <paramref name="clientJobId"/>, as someone removing it in the client does.</summary>
    void Remove(string clientJobId);

    /// <summary>Makes every request fail as one to a port nothing listens on.</summary>
    void MakeUnreachable();

    /// <summary>Changes the client's password, so the one the settings carry is refused from now on.</summary>
    void RefuseCredentials();

    /// <summary>Forgets every session the client handed out, as a restart does.</summary>
    void ForgetSessions();

    /// <summary>Forgets the client's sessions right before it would read the next add (ADR 0013, D8).</summary>
    void ForgetSessionsBeforeTheNextAdd();

    /// <summary>Lets every add's answer never arrive.</summary>
    /// <param name="arrived">Run as each stalled add reaches the client — to cancel the caller's token once the add is
    /// sent, say.</param>
    void StallAdds(Action? arrived = null);

    /// <summary>Lets every answer never arrive.</summary>
    /// <param name="arrived">Run as each stalled request reaches the client.</param>
    void StallEverything(Action? arrived = null);
}
