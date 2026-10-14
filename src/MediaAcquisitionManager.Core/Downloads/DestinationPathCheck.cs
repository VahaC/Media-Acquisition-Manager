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
/// <para>
/// MAM-30 — a client whose destinations are folders (ADR 0039) lists each folder the app proposed, with the client's
/// sentence when it can't use one: that is <see cref="DestinationPathStatus.FolderNotSeen"/>, which still names the
/// libraries the folder would be in, so the owner knows which one is unreachable, but gives no app path, so nothing
/// treats it as a place to check or send to.
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
    /// of it. MAM-30 — for a <see cref="DownloadDestinationKind.Folder"/> key, the listed folder that is the same
    /// folder, and <see cref="DestinationPathStatus.FolderNotSeen"/> when none is.
    /// </summary>
    /// <param name="clientDestinationKey">The key the destination is stored with.</param>
    /// <param name="clientDestinations">The client's destinations.</param>
    /// <param name="clientPathMappings">The client integration's path mappings.</param>
    /// <param name="libraryFolders">The media library folders, already inside the app.</param>
    /// <param name="kind">MAM-30 — what the client's destinations are: a category is found by its exact name, a folder
    /// by <see cref="NormalizedPath.IsSameAs"/>.</param>
    /// <param name="clientName">MAM-30 — the client's name, for the sentence of a folder it didn't list; "the client"
    /// when not given.</param>
    public static DestinationPathCheckResult Check(
        string clientDestinationKey,
        IReadOnlyList<ClientDestination> clientDestinations,
        IReadOnlyList<PathMappingPair> clientPathMappings,
        IReadOnlyList<LibraryFolder> libraryFolders,
        DownloadDestinationKind kind = DownloadDestinationKind.Category,
        string? clientName = null)
    {
        if (kind == DownloadDestinationKind.Folder)
            return CheckFolder(clientDestinationKey, clientDestinations, clientPathMappings, libraryFolders, clientName);

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
    /// client's path mappings, against the media library folders. MAM-30 — one the client says it can't use
    /// (<see cref="ClientDestination.Problem"/>) is <see cref="DestinationPathStatus.FolderNotSeen"/> with that sentence.
    /// </summary>
    /// <param name="destination">One destination the client listed.</param>
    /// <param name="clientPathMappings">The client integration's path mappings.</param>
    /// <param name="libraryFolders">The media library folders, already inside the app.</param>
    public static DestinationPathCheckResult Check(
        ClientDestination destination,
        IReadOnlyList<PathMappingPair> clientPathMappings,
        IReadOnlyList<LibraryFolder> libraryFolders)
    {
        // MAM-30 — before anything else: the client's word that it can't use the folder outweighs where the folder
        // would be (D5).
        if (destination.Problem is { } problem)
            return NotSeen(destination.ServicePath, clientPathMappings, libraryFolders, problem);

        if (string.IsNullOrWhiteSpace(destination.ServicePath))
            return new DestinationPathCheckResult(DestinationPathStatus.PathUnknown, null, null, [], ServicePathUnknownError);

        var mapped = PathMapper.Map(destination.ServicePath, clientPathMappings);
        if (mapped.AppPath is null)
            return new DestinationPathCheckResult(DestinationPathStatus.PathUnknown, destination.ServicePath, null, [], WithConsequence(mapped.Reason!));

        // Before the folders are compared: with none stored yet, "outside every library" would be a
        // claim about libraries the app has never read.
        if (libraryFolders.Count == 0)
            return new DestinationPathCheckResult(DestinationPathStatus.LibrariesUnknown, destination.ServicePath, mapped.AppPath, [], LibrariesUnknownError);

        var libraryNames = LibrariesHolding(mapped.AppPath, libraryFolders);
        return libraryNames.Count > 0
            ? new DestinationPathCheckResult(DestinationPathStatus.InLibrary, destination.ServicePath, mapped.AppPath, libraryNames, null)
            : new DestinationPathCheckResult(
                DestinationPathStatus.OutsideLibraries,
                destination.ServicePath,
                mapped.AppPath,
                [],
                $"“{mapped.AppPath}” is outside every media library folder, {Consequence}.");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MAM-30 — a stored folder among what a folder client listed. One folder however it is spelt (D2): a client may
    /// echo <c>/downloads/movies/</c> for the stored <c>/downloads/movies</c>, and a Windows client either letter case.
    /// A stored folder that isn't a full path names no place the client could confirm, and one the client didn't list
    /// wasn't confirmed: either is this row's problem alone, never a failure of the whole client (D3).
    /// </summary>
    private static DestinationPathCheckResult CheckFolder(
        string clientDestinationKey,
        IReadOnlyList<ClientDestination> clientDestinations,
        IReadOnlyList<PathMappingPair> clientPathMappings,
        IReadOnlyList<LibraryFolder> libraryFolders,
        string? clientName)
    {
        var client = clientName is null ? "the client" : $"“{clientName}”";
        if (!NormalizedPath.TryParse(clientDestinationKey, out var folder, out _))
        {
            return new DestinationPathCheckResult(
                DestinationPathStatus.FolderNotSeen,
                null,
                null,
                [],
                $"The app can't use the folder “{clientDestinationKey}” of this destination: it isn't a full path in {client}.");
        }

        // A listed key that doesn't parse names no folder, so it never matches one.
        var destination = clientDestinations.FirstOrDefault(candidate =>
            NormalizedPath.TryParse(candidate.Key, out var listed, out _) && listed.IsSameAs(folder));
        return destination is null
            ? NotSeen(
                folder.ToString(),
                clientPathMappings,
                libraryFolders,
                $"The app can't use the folder “{clientDestinationKey}” of this destination: {client} didn't list it.")
            : Check(destination, clientPathMappings, libraryFolders);
    }

    /// <summary>
    /// MAM-30 — a folder the client can't use: its folder as the client sees it and the libraries it would be in, for
    /// the owner to tell which one it is, but never an app path (D5).
    /// </summary>
    private static DestinationPathCheckResult NotSeen(
        string? servicePath, IReadOnlyList<PathMappingPair> clientPathMappings, IReadOnlyList<LibraryFolder> libraryFolders, string problem)
    {
        var libraryNames = !string.IsNullOrWhiteSpace(servicePath) && PathMapper.Map(servicePath, clientPathMappings) is { AppPath: { } appPath }
            ? LibrariesHolding(appPath, libraryFolders)
            : [];
        return new DestinationPathCheckResult(DestinationPathStatus.FolderNotSeen, servicePath, null, libraryNames, problem);
    }

    /// <summary>The libraries one of whose folders holds <paramref name="appPath"/>, each once, in the order of their
    /// first folder that does.</summary>
    private static IReadOnlyList<string> LibrariesHolding(string appPath, IReadOnlyList<LibraryFolder> libraryFolders)
    {
        // The mapper writes the app path in canonical form, which always reads back. A library folder
        // that doesn't parse holds nothing.
        var path = NormalizedPath.TryParse(appPath, out var parsed, out _) ? parsed : null;
        var libraryNames = new List<string>();
        foreach (var folder in libraryFolders)
        {
            if (path is not null
                && NormalizedPath.TryParse(folder.AppPath, out var root, out _)
                && path.IsUnder(root)
                && !libraryNames.Contains(folder.LibraryName, StringComparer.Ordinal))
                libraryNames.Add(folder.LibraryName);
        }

        return libraryNames.AsReadOnly();
    }

    // The mapper's sentence and what follows from it, as one sentence.
    private static string WithConsequence(string reason) => $"{(reason.EndsWith('.') ? reason[..^1] : reason)}, {Consequence}.";
}
