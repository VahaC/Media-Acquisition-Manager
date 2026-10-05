using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

/// <summary>MAM-33 — one stored reason of a <see cref="StoredRecommendation"/>, as its row holds it.</summary>
/// <param name="Kind">What kind of reason.</param>
/// <param name="Name">The collection, person or source named.</param>
/// <param name="Held">What the library holds of it.</param>
/// <param name="Total">The collection's released parts.</param>
/// <param name="Titles">The recommending library works' titles, <c>'\n'</c>-joined.</param>
/// <param name="Seasons">The missing seasons, "4,5".</param>
/// <param name="DateUtc">When the source published it.</param>
public sealed record StoredRecommendationReason(
    RecommendationReasonKind Kind,
    string? Name = null,
    int? Held = null,
    int? Total = null,
    string? Titles = null,
    string? Seasons = null,
    DateTime? DateUtc = null);
