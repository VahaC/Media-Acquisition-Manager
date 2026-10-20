using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Where a destination saves to and whether that is inside a media library (MAM-11): worked out live from
/// what the client says and the stored library folders, never stored (ADR 0003).
/// </summary>
/// <param name="Status">Where the folder is, or why the app can't say.</param>
/// <param name="ServicePath">The folder as the client sees it; <c>null</c> when the client doesn't say
/// or wasn't asked.</param>
/// <param name="AppPath">The same folder inside the app, through the client's path mappings; <c>null</c>
/// when they give none. MAM-30 — always <c>null</c> for <see cref="DestinationPathStatus.FolderNotSeen"/>, even when
/// the mappings give one: the client can't use the folder, so nothing may treat it as a place to check or send
/// to.</param>
/// <param name="LibraryNames">The libraries one of whose folders holds it, each once, in the order of
/// their first folder that does; empty unless <see cref="DestinationPathStatus.InLibrary"/> or — MAM-30 —
/// <see cref="DestinationPathStatus.FolderNotSeen"/>, where they name the folder the client can't see.</param>
/// <param name="Problem">What is wrong and what follows from it, as a message the Api renders (ADR 0042); <c>null</c>
/// only for <see cref="DestinationPathStatus.InLibrary"/>.</param>
public sealed record DestinationPathCheckResult(
    DestinationPathStatus Status,
    string? ServicePath,
    string? AppPath,
    IReadOnlyList<string> LibraryNames,
    Message? Problem)
{
    /// <summary>
    /// The download client integration is switched off, so nothing was asked; <paramref name="problem"/>
    /// says so in the words of the page that shows it.
    /// </summary>
    public static DestinationPathCheckResult ClientDisabled(Message problem) =>
        new(DestinationPathStatus.ClientDisabled, null, null, [], problem);

    /// <summary>
    /// The client couldn't be asked; <paramref name="problem"/> is why — the adapter's message, or the
    /// app's own when the type isn't registered or the stored secret can't be read.
    /// </summary>
    public static DestinationPathCheckResult ClientUnavailable(Message problem) =>
        new(DestinationPathStatus.ClientUnavailable, null, null, [], problem);
}
