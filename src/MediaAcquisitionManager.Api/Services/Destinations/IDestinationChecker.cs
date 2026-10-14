using MediaAcquisitionManager.Core.Downloads;
using MediaAcquisitionManager.Core.Entities;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>
/// Works out where destinations save and whether that is inside a media library (MAM-11, ADR 0003): asks
/// the download clients live for their destinations and checks each against the media library folders
/// the last syncs stored (<see cref="DestinationPathCheck"/>), every folder through its own integration's
/// current path mappings. Nothing it works out is stored, so an edited mapping, a resynced library or a
/// category changed in the client shows the next time anyone looks.
/// </summary>
/// <remarks>
/// <para>
/// Reads only: a client is asked to list its destinations and nothing else. A client that couldn't be
/// asked is an answer, never an exception — switched off, a type this version can't send downloads to, a
/// stored secret it can't read, or the client's own failure, its sentence masked of every secret of the
/// call. Only the caller's cancellation propagates. It checks nothing about who calls.
/// </para>
/// <para>
/// MAM-30 — a client whose destinations are folders (ADR 0039) has no list of its own: the app proposes the media
/// library folders, each mapped back through the client's path mappings, and the stored folders the caller names, and
/// the client confirms each or says why it can't use it. A stored folder is so checked as itself at every look (D3),
/// never found missing because a mapping changed; one that isn't a full path is never proposed and is that
/// destination's problem alone. A library folder the client has no folder for is listed apart, with how to fix it.
/// </para>
/// </remarks>
public interface IDestinationChecker
{
    /// <summary>
    /// The check of each of <paramref name="destinations"/>, by its id. Each download client among them is
    /// asked once however many of them it holds, every enabled one at the same time; a switched-off one
    /// isn't asked (<see cref="DestinationPathStatus.ClientDisabled"/>), and one that couldn't be asked
    /// gives each of its destinations <see cref="DestinationPathStatus.ClientUnavailable"/> with the
    /// reason. MAM-30 — a folder client is proposed its destinations' stored folders.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DestinationPathCheckResult>> CheckAsync(
        IReadOnlyCollection<DestinationMappingEntity> destinations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks <paramref name="client"/>, a download client integration read with its path mappings, for its
    /// destinations, each checked — or why it couldn't be asked, in the order a save checks it: switched
    /// off, a type this version can't send downloads to, a stored secret it can't read, then the call.
    /// </summary>
    /// <param name="client">The download client integration, with its path mappings.</param>
    /// <param name="storedKeys">MAM-30 — the stored keys of the destinations the caller is about — the one a send, an
    /// overwrite check or a replacement's options read, the one the Edit dialog edits —, which a folder client is
    /// proposed too and each of which is then listed, the client's word on it or, for a folder it couldn't be proposed,
    /// why. None for a save's confirmation: a save may only pick what the client offers (D4). A category client ignores
    /// them.</param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    Task<ClientDestinationsResult> ListAsync(
        IntegrationConnectionEntity client, IReadOnlyCollection<string> storedKeys, CancellationToken cancellationToken = default);
}
