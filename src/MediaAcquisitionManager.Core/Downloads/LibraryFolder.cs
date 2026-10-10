namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// One root folder of a media library, already mapped to where the app sees it (MAM-11): what a
/// destination's folder is checked against (<see cref="DestinationPathCheck"/>). A folder whose path
/// mappings give no app path is left out by whoever builds the list: it can't contain anything the app
/// can name.
/// </summary>
/// <param name="LibraryName">The library's name ("Фільми").</param>
/// <param name="AppPath">The folder inside the app, in <see cref="Paths.NormalizedPath"/> canonical form
/// (<c>/media/movies</c>).</param>
/// <param name="IntegrationId">
/// MAM-25 — the media library integration that reported the folder: the one a replacement's new file in it is asked of
/// (ADR 0035). <c>null</c> when whoever built the list didn't say, as a destination's check needs only the name.
/// </param>
/// <param name="ServicePath">
/// MAM-25 — the folder in the library's own terms, as it reported it: a new file's path in the library is this followed
/// by the file's folders under <paramref name="AppPath"/>, with no mapping run backwards. <c>null</c> likewise.
/// </param>
public sealed record LibraryFolder(string LibraryName, string AppPath, Guid? IntegrationId = null, string? ServicePath = null);
