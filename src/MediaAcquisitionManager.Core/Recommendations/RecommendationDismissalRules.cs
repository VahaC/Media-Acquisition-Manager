using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — which work a "Not interested" mark (Vasyl's decision 3) may name: a film or a show by its kind, and its
/// catalog id. One readable sentence for a value refused, <c>null</c> for one accepted — the MAM-4 rules-class pattern.
/// </summary>
/// <remarks>
/// The id is checked as an id of the catalog in use (TMDb's: 1 to 12 digits, <see cref="MediaIdentifierRules.IsTmdbId"/>),
/// not looked up: a mark outlives the work's catalog row, and an undo of a mark nobody made changes nothing.
/// </remarks>
public static class RecommendationDismissalRules
{
    /// <summary>How much of an id that isn't one a sentence repeats, as the other rules do.</summary>
    private const int EchoLength = 32;

    /// <summary>
    /// The sentence saying what is wrong with <paramref name="kind"/>, then <paramref name="catalogId"/>, or <c>null</c>
    /// with the kind and the id, trimmed, in <paramref name="workKind"/> and <paramref name="workCatalogId"/>.
    /// </summary>
    public static string? Validate(string? kind, string? catalogId, out MediaWorkKind? workKind, out string? workCatalogId)
    {
        workCatalogId = null;
        if (RecommendationListRules.ValidateKind(kind, out workKind) is { } kindError)
            return kindError;

        var id = catalogId?.Trim() ?? "";
        if (!MediaIdentifierRules.IsTmdbId(id))
        {
            workKind = null;
            return $"“{Echo(id)}” is not a catalog id, which is a number such as 603.";
        }

        workCatalogId = id;
        return null;
    }

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
