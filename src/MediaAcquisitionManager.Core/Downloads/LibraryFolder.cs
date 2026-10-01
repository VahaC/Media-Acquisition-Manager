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
public sealed record LibraryFolder(string LibraryName, string AppPath);
