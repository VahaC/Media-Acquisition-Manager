using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationSettingsService.SaveAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Settings">The settings as saved when <see cref="RecommendationSettingsSaveStatus.Saved"/>.</param>
/// <param name="Error">Why, when <see cref="RecommendationSettingsSaveStatus.Invalid"/>, as a message the Api renders
/// (ADR 0042).</param>
public sealed record RecommendationSettingsSaveResult(RecommendationSettingsSaveStatus Status, RecommendationSettingsDto? Settings, Message? Error)
{
    public static RecommendationSettingsSaveResult Saved(RecommendationSettingsDto settings) =>
        new(RecommendationSettingsSaveStatus.Saved, settings, null);

    public static RecommendationSettingsSaveResult Invalid(Message error) => new(RecommendationSettingsSaveStatus.Invalid, null, error);
}
