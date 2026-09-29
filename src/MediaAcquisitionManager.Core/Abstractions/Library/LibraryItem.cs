using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>
/// One item of a library as the service reports it. An item is not a work: the core decides which
/// work it belongs to (ADR 0006).
/// </summary>
/// <param name="SourceItemId">The library's own id of the item.</param>
/// <param name="Kind">What the item is.</param>
/// <param name="Titles">The titles the library gives it.</param>
/// <param name="Identifiers">The registry ids the library claims for it.</param>
/// <param name="Versions">Its versions: one, or several when the library groups them.</param>
public sealed record LibraryItem(
    string SourceItemId,
    MediaWorkKind Kind,
    IReadOnlyList<ReportedTitle> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<LibraryVersion> Versions);
