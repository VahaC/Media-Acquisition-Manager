using MediaAcquisitionManager.Core.Abstractions.Media;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// MAM-25 — what the library holds at one path, read by <see cref="IMediaLibraryFileLookup.ReadFilesAsync"/>: the file's
/// own item — a version the library keeps hidden under another item included —, and what the check of a replacement's new
/// file compares (specification §3.4, checks 3 and 4). An item is not a work: the core decides which work it is.
/// </summary>
/// <param name="ServicePath">The path as asked, exactly: the library's own spelling of the file.</param>
/// <param name="Listed">Whether the library holds an item for exactly this file. A file it doesn't hold (yet) is a
/// reading with this <c>false</c> and nothing else, never a failure of the read.</param>
/// <param name="ItemId">The library's own id of the file's item; <c>null</c> when not <paramref name="Listed"/>.</param>
/// <param name="Kind">What the library made of the file; <c>null</c> when not <paramref name="Listed"/>.</param>
/// <param name="Refreshed">Whether the library has finished reading the file: it measured its running time and, for an
/// episode, numbered it. A file it has only just made an item of carries neither, nor any id yet. Always <c>false</c> when
/// not <paramref name="Listed"/>.</param>
/// <param name="Identifiers">The registry ids the library claims for the item itself, read the way the sync reads them: a
/// film's IMDb and TMDb ids, an episode's own IMDb and TVDB ids; none for <see cref="LibraryFileKind.Other"/>.</param>
/// <param name="Episode">Where an episode item sits; <c>null</c> for any other kind.</param>
/// <param name="SeriesIdentifiers">An episode's series item's registry ids, read with the series' rules (its TMDb id a
/// show's, <see cref="Enums.MediaIdentifierScheme.TmdbTv"/>): the ids that say which show the episode belongs to. None when
/// it has no series item, the series has none, or the item is not an episode.</param>
public sealed record LibraryFileReading(
    string ServicePath,
    bool Listed,
    string? ItemId,
    LibraryFileKind? Kind,
    bool Refreshed,
    IReadOnlyList<MediaIdentifier> Identifiers,
    LibraryEpisodePlace? Episode,
    IReadOnlyList<MediaIdentifier> SeriesIdentifiers);
