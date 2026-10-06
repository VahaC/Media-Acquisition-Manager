using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — what one <see cref="IMediaLibraryIdentifierWriter.WriteIdentifiersAsync"/> found and did.</summary>
/// <param name="Before">The item's well-formed ids of the edited schemes as read before the write: the audit row's
/// Before, and what a restore puts back.</param>
/// <param name="After">The same schemes as read again after it; when that read never came, empty.</param>
/// <param name="Applied">The schemes whose edit changed something (its rule held).</param>
/// <param name="Sent">Whether the item was sent back: <c>false</c> when no edit changed anything, so nothing was
/// posted.</param>
/// <param name="Confirmed">Whether the read after the write shows every applied edit (or, with nothing sent, whether
/// the item already holds what was wanted).</param>
/// <param name="Problem">R10 — a readable sentence saying why the write isn't confirmed (its answer lost, a timeout
/// after sending, a read afterwards that failed or disagreed), the call's secrets masked; <c>null</c> when it is.</param>
public sealed record LibraryIdentifierWrite(
    IReadOnlyList<MediaIdentifier> Before,
    IReadOnlyList<MediaIdentifier> After,
    IReadOnlyList<MediaIdentifierScheme> Applied,
    bool Sent,
    bool Confirmed,
    string? Problem = null);
