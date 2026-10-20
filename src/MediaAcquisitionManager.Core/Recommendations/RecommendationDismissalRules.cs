using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Media;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Recommendations;

/// <summary>
/// MAM-33 — which work a "Not interested" mark (Vasyl's decision 3) may name: a film or a show by its kind, and its
/// catalog id. A message the Api renders (ADR 0042) for a value refused, <c>null</c> for one accepted — the MAM-4
/// rules-class pattern.
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
    /// Why <paramref name="kind"/>, then <paramref name="catalogId"/>, is refused, or <c>null</c>
    /// with the kind and the id, trimmed, in <paramref name="workKind"/> and <paramref name="workCatalogId"/>.
    /// </summary>
    public static Message? Validate(string? kind, string? catalogId, out MediaWorkKind? workKind, out string? workCatalogId)
    {
        workCatalogId = null;
        if (RecommendationListRules.ValidateKind(kind, out workKind) is { } kindError)
            return kindError;

        var id = catalogId?.Trim() ?? "";
        if (!MediaIdentifierRules.IsTmdbId(id))
        {
            workKind = null;
            return Message.Of("recommendations.dismissal.catalog-id-invalid", ("value", Echo(id)));
        }

        workCatalogId = id;
        return null;
    }

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength), "…");
}
