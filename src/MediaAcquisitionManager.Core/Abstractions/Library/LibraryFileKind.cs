namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-25 — what a library made of one file, by its word alone (ADR 0017): the kind of the file's own item, read by
/// <see cref="IMediaLibraryFileLookup.ReadFilesAsync"/>. A replacement's new-file evidence keeps it stored by name
/// (ADR 0035), so a member added later needs no migration.
/// </summary>
public enum LibraryFileKind
{
    /// <summary>A film item: its registry ids are a film's.</summary>
    Movie = 1,

    /// <summary>An episode item: its place is a season and an episode number of a series item, and its series' ids say
    /// which show it belongs to.</summary>
    Episode = 2,

    /// <summary>Anything else the library made of the file — a plain video, an extra, a home video: no work of the
    /// registries, so it is never taken for a film or an episode.</summary>
    Other = 3,
}
