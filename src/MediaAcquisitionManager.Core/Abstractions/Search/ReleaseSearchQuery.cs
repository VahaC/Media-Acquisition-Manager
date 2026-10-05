using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>What to search a source for, already decided by the core: an adapter only translates it.</summary>
/// <remarks>
/// MAM-10 — the core decides (<c>ReleaseSearchPlanning</c>, from the source's capabilities), so which
/// identifier or year a source can take is one tested rule, not a guess each adapter makes its own way.
/// Every field is sent as given: an identifier or a year the source doesn't take never reaches it.
/// </remarks>
/// <param name="Mode">Which of the source's searches to use.</param>
/// <param name="Text">The search text; <c>null</c> when the query searches by an identifier alone, and always
/// with <see cref="ReleaseSearchMode.Latest"/> (MAM-33).</param>
/// <param name="Year">The year, sent as a parameter of its own; <c>null</c> when none is sent.</param>
/// <param name="Identifiers">Registry ids to search by; only with <see cref="ReleaseSearchMode.Movie"/> or
/// <see cref="ReleaseSearchMode.Tv"/> (MAM-32).</param>
/// <param name="CategoryIds">The source's categories to search in; empty for all. A hit the source
/// lists in none of them is left out, whether or not the source honoured the filter.</param>
/// <param name="Limit">The most releases to ask for and to return.</param>
/// <param name="Season">The season to search for, sent as a parameter of its own; only with
/// <see cref="ReleaseSearchMode.Tv"/>, and <c>null</c> when none is sent (MAM-32).</param>
public sealed record ReleaseSearchQuery(
    ReleaseSearchMode Mode,
    string? Text,
    int? Year,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<string> CategoryIds,
    int Limit,
    int? Season = null);
