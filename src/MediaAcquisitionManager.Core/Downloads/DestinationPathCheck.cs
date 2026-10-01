using MediaAcquisitionManager.Core.Abstractions.Downloads;
using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// Whether a destination saves inside a media library (MAM-11, ADR 0003, consequences): the folder the
/// client says the destination saves to, mapped through the client's path mappings, must lie inside the
/// app path of a media library's root folder — folder name by folder name
/// (<see cref="NormalizedPath.IsUnder"/>), the root folder itself included.
/// </summary>
/// <remarks>
/// <para>
/// Pure: the caller asks the client for its destinations and maps the stored library folders through
/// their own integration's mappings; nothing here is stored, so an edited mapping or a resynced library
/// is seen the next time anyone looks.
/// </para>
/// <para>
/// No guess anywhere. A client that doesn't say where a destination saves, a mapping that gives no app
/// path, and library folders not known yet are each a status of their own, and every status but
/// <see cref="DestinationPathStatus.InLibrary"/> keeps automatic replacement off for the destination
/// (Phase C). A folder that only contains a library folder (<c>/media</c> above <c>/media/movies</c>) is
/// outside it: what lands there isn't in the library.
/// </para>
/// </remarks>
public static class DestinationPathCheck
{
    private const string Consequence = "so automatic replacement stays off for this destination";

    private const string ServicePathUnknownError = "The client doesn't say which folder this category saves to, " + Consequence + ".";

    private const string LibrariesUnknownError =
        "The app doesn't know the media library folders yet. Sync a library from the Library page, then look again.";

    /// <summary>
    /// Checks the destination the client knows as <paramref name="clientDestinationKey"/> among
    /// <paramref name="clientDestinations"/>, what the client has just listed:
    /// <see cref="DestinationPathStatus.CategoryMissing"/> when none has exactly that key, else
    /// <see cref="Check(ClientDestination, IReadOnlyList{PathMappingPair}, IReadOnlyList{LibraryFolder})"/>
    /// of it.
    /// </summary>
    /// <param name="clientDestinationKey">The key the destination is stored with.</param>
    /// <param name="clientDestinations">The client's destinations.</param>
    /// <param name="clientPathMappings">The client integration's path mappings.</param>
    /// <param name="libraryFolders">The media library folders, already inside the app.</param>
    public static DestinationPathCheckResult Check(
        string clientDestinationKey,
        IReadOnlyList<ClientDestination> clientDestinations,
        IReadOnlyList<PathMappingPair> clientPathMappings,
        IReadOnlyList<LibraryFolder> libraryFolders)
    {
        // Exactly the client's own name, letter case and spaces included: it is what a submission sends
        // (DestinationRules), and a near match would send downloads somewhere the owner never chose.
        var destination = clientDestinations.FirstOrDefault(candidate => string.Equals(candidate.Key, clientDestinationKey, StringComparison.Ordinal));
        return destination is null
            ? new DestinationPathCheckResult(
                DestinationPathStatus.CategoryMissing, null, null, [], $"The client has no category “{clientDestinationKey}” any more.")
            : Check(destination, clientPathMappings, libraryFolders);
    }

    /// <summary>
    /// Checks one of the client's destinations, as a picker lists them: where it saves, through the
    /// client's path mappings, against the media library folders.
    /// </summary>
    /// <param name="destination">One destination the client listed.</param>
    /// <param name="clientPathMappings">The client integration's path mappings.</param>
    /// <param name="libraryFolders">The media library folders, already inside the app.</param>
    public static DestinationPathCheckResult Check(
        ClientDestination destination,
        IReadOnlyList<PathMappingPair> clientPathMappings,
        IReadOnlyList<LibraryFolder> libraryFolders)
    {
        if (string.IsNullOrWhiteSpace(destination.ServicePath))
            return new DestinationPathCheckResult(DestinationPathStatus.PathUnknown, null, null, [], ServicePathUnknownError);

        var mapped = PathMapper.Map(destination.ServicePath, clientPathMappings);
        if (mapped.AppPath is null)
            return new DestinationPathCheckResult(DestinationPathStatus.PathUnknown, destination.ServicePath, null, [], WithConsequence(mapped.Reason!));

        // Before the folders are compared: with none stored yet, "outside every library" would be a
        // claim about libraries the app has never read.
        if (libraryFolders.Count == 0)
            return new DestinationPathCheckResult(DestinationPathStatus.LibrariesUnknown, destination.ServicePath, mapped.AppPath, [], LibrariesUnknownError);

        // The mapper writes the app path in canonical form, which always reads back. A library folder
        // that doesn't parse holds nothing.
        var appPath = NormalizedPath.TryParse(mapped.AppPath, out var parsed, out _) ? parsed : null;
        var libraryNames = new List<string>();
        foreach (var folder in libraryFolders)
        {
            if (appPath is not null
                && NormalizedPath.TryParse(folder.AppPath, out var root, out _)
                && appPath.IsUnder(root)
                && !libraryNames.Contains(folder.LibraryName, StringComparer.Ordinal))
                libraryNames.Add(folder.LibraryName);
        }

        return libraryNames.Count > 0
            ? new DestinationPathCheckResult(DestinationPathStatus.InLibrary, destination.ServicePath, mapped.AppPath, libraryNames.AsReadOnly(), null)
            : new DestinationPathCheckResult(
                DestinationPathStatus.OutsideLibraries,
                destination.ServicePath,
                mapped.AppPath,
                [],
                $"“{mapped.AppPath}” is outside every media library folder, {Consequence}.");
    }

    // The mapper's sentence and what follows from it, as one sentence.
    private static string WithConsequence(string reason) => $"{(reason.EndsWith('.') ? reason[..^1] : reason)}, {Consequence}.";
}
