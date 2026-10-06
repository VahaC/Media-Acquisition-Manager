using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — what one write into a library item's ids changes (<see cref="IMediaLibraryIdentifierWriter"/>).</summary>
/// <param name="SourceItemId">The library's id of the item.</param>
/// <param name="Kind">What the item must be — a film (<see cref="MediaWorkKind.Movie"/>) or a show
/// (<see cref="MediaWorkKind.Series"/>), and a kind the writer's <see cref="IMediaLibraryIdentifierWriter.WritableKinds"/>
/// holds; the library holding an item of another type under the id is refused unwritten.</param>
/// <param name="Edits">The ids to set or remove, one per scheme, at least one.</param>
public sealed record LibraryIdentifierChange(string SourceItemId, MediaWorkKind Kind, IReadOnlyList<LibraryIdentifierEdit> Edits);
