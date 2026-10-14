using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>What <see cref="LibraryFolderReader.LoadAsync"/> read: the stored library folders that map into the app,
/// and how many were stored in all — MAM-30, and those that don't, with why.</summary>
/// <param name="Mapped">Each stored folder whose mappings give it an app path, one entry per library that lists it, by
/// library name, then the library, then the server's order of its folders; a folder two libraries list is here twice.</param>
/// <param name="StoredCount">Every stored folder, mapped or not.</param>
/// <param name="Unmapped">MAM-30 — each stored folder whose mappings give it no app path, in the same order; only a
/// download client whose destinations are folders reads them (ADR 0039), every other reader ignores them.</param>
public sealed record LibraryFolders(IReadOnlyList<LibraryFolder> Mapped, int StoredCount, IReadOnlyList<UnmappedLibraryFolder> Unmapped);
