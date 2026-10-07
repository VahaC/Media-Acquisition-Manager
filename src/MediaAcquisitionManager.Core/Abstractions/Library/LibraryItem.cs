using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// One item of a library as the service reports it. An item is not a work: the core decides which
/// work it belongs to (ADR 0006).
/// </summary>
/// <param name="SourceItemId">The library's own id of the item.</param>
/// <param name="Kind">What the item is, by the library's word alone (ADR 0017).</param>
/// <param name="Titles">The titles the library gives it.</param>
/// <param name="Identifiers">The registry ids the library claims for it.</param>
/// <param name="Versions">Its versions: one, or several when the library groups them. None for a
/// series.</param>
/// <param name="Episode">Where an episode sits in its series (MAM-32); <c>null</c> for a movie or a
/// series.</param>
/// <param name="IsMissing">Whether the library only knows of the item and holds no file for it — a
/// missing episode it lists from the show's metadata (MAM-32). Read like any other, so the pages of the
/// list still add up, and stored as nothing (ADR 0017).</param>
/// <param name="PosterTag">MAM-35 — the library's own name of the item's current poster image, which changes whenever
/// the image does; <c>null</c> when the item has none. Always the item's own image, never its series' or season's lent to
/// an episode: a card shows the picture of the item it stands for, or none. The poster itself is fetched with it
/// (<see cref="IMediaLibraryProvider.GetPosterAsync"/>).</param>
public sealed record LibraryItem(
    string SourceItemId,
    MediaWorkKind Kind,
    IReadOnlyList<ReportedTitle> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<LibraryVersion> Versions,
    LibraryEpisode? Episode = null,
    bool IsMissing = false,
    string? PosterTag = null);
