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
/// Reads only: a client is asked to list its destinations and nothing else. A client that couldn't be
/// asked is an answer, never an exception — switched off, a type this version can't send downloads to, a
/// stored secret it can't read, or the client's own failure, its sentence masked of every secret of the
/// call. Only the caller's cancellation propagates. It checks nothing about who calls.
/// </remarks>
public interface IDestinationChecker
{
    /// <summary>
    /// The check of each of <paramref name="destinations"/>, by its id. Each download client among them is
    /// asked once however many of them it holds, every enabled one at the same time; a switched-off one
    /// isn't asked (<see cref="DestinationPathStatus.ClientDisabled"/>), and one that couldn't be asked
    /// gives each of its destinations <see cref="DestinationPathStatus.ClientUnavailable"/> with the
    /// reason.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DestinationPathCheckResult>> CheckAsync(
        IReadOnlyCollection<DestinationMappingEntity> destinations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks <paramref name="client"/>, a download client integration read with its path mappings, for its
    /// destinations, each checked — or why it couldn't be asked, in the order a save checks it: switched
    /// off, a type this version can't send downloads to, a stored secret it can't read, then the call.
    /// </summary>
    Task<ClientDestinationsResult> ListAsync(IntegrationConnectionEntity client, CancellationToken cancellationToken = default);
}
