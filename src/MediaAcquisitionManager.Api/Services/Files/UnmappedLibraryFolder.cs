namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// MAM-30 — a stored library folder its own integration's path mappings bring nowhere into the app: what
/// <see cref="LibraryFolderReader.LoadAsync"/> leaves out of <see cref="LibraryFolders.Mapped"/>, kept so a download client
/// whose destinations are the library folders can say which library it has no folder for and why, rather than leave
/// the library out of its list (ADR 0039).
/// </summary>
/// <param name="LibraryName">The library's name ("Фільми").</param>
/// <param name="ServicePath">The folder as the library reported it (<c>/data/movies</c>).</param>
/// <param name="IntegrationName">The media library integration that reported it, whose mappings are what to fix.</param>
/// <param name="Reason">The mapper's sentence saying why it has no app path.</param>
public sealed record UnmappedLibraryFolder(string LibraryName, string ServicePath, string IntegrationName, string Reason);
