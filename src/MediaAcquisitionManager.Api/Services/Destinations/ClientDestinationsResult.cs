using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>The outcome of <see cref="IDestinationChecker.ListAsync"/>: what a download client offers
/// right now, or why it couldn't be asked (MAM-11).</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints the lists as their type names, and the error is masked
/// of every secret of the call before it gets here.
/// </remarks>
/// <param name="Status">Whether the client was asked, and how that went.</param>
/// <param name="Destinations">Its destinations in its own order, each checked, when
/// <see cref="ClientDestinationsStatus.Listed"/>; empty otherwise.</param>
/// <param name="Error">Why the client couldn't be asked, as a message the Api renders (ADR 0042); <c>null</c> when it
/// answered.</param>
/// <param name="Kind">MAM-30 — what the client's destinations are, by its type; <c>null</c> for a type this build
/// doesn't register.</param>
/// <param name="Unplaced">MAM-30 — for a client whose destinations are folders, the library folders it has no folder
/// for, each with why, by library name; empty otherwise, and whenever the client wasn't listed.</param>
public sealed record ClientDestinationsResult(
    ClientDestinationsStatus Status,
    IReadOnlyList<CheckedClientDestination> Destinations,
    Message? Error,
    DownloadDestinationKind? Kind,
    IReadOnlyList<UnplacedLibraryFolder> Unplaced)
{
    public static ClientDestinationsResult Listed(
        DownloadDestinationKind? kind, IReadOnlyList<CheckedClientDestination> destinations, IReadOnlyList<UnplacedLibraryFolder> unplaced) =>
        new(ClientDestinationsStatus.Listed, destinations, null, kind, unplaced);

    public static ClientDestinationsResult NotListed(ClientDestinationsStatus status, Message error, DownloadDestinationKind? kind) =>
        new(status, [], error, kind, []);

    /// <summary>
    /// The listed destination a destination stored with <paramref name="clientDestinationKey"/> is; <c>null</c> when
    /// none is. A category by its exact name (ADR 0010); MAM-30 — a folder by <see cref="NormalizedPath.IsSameAs"/>, as a
    /// client may echo it in another spelling (D2), or, for a stored folder that isn't a full path, by its exact text.
    /// </summary>
    public CheckedClientDestination? Find(string clientDestinationKey) =>
        Destinations.FirstOrDefault(listed => Kind == DownloadDestinationKind.Folder
            ? IsSameFolder(listed.Destination.Key, clientDestinationKey)
            : string.Equals(listed.Destination.Key, clientDestinationKey, StringComparison.Ordinal));

    /// <summary>MAM-30 — whether two folder keys name one folder: both full paths that are the same, or the same text.</summary>
    public static bool IsSameFolder(string key, string other) =>
        string.Equals(key, other, StringComparison.Ordinal)
        || (NormalizedPath.TryParse(key, out var path, out _) && NormalizedPath.TryParse(other, out var otherPath, out _) && path.IsSameAs(otherPath));
}
