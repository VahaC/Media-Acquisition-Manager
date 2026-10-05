using MediaAcquisitionManager.Core.Recommendations;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — one user's recommendation settings as they hold: the row they saved, or the defaults.</summary>
/// <param name="UserId">The user.</param>
/// <param name="Settings">Their settings.</param>
public sealed record UserRecommendationSettings(Guid UserId, RecommendationSettings Settings);
