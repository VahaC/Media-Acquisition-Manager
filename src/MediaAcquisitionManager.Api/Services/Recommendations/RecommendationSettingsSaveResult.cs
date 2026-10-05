using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationSettingsService.SaveAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Settings">The settings as saved when <see cref="RecommendationSettingsSaveStatus.Saved"/>.</param>
/// <param name="Error">The sentence when <see cref="RecommendationSettingsSaveStatus.Invalid"/>.</param>
public sealed record RecommendationSettingsSaveResult(RecommendationSettingsSaveStatus Status, RecommendationSettingsDto? Settings, string? Error)
{
    public static RecommendationSettingsSaveResult Saved(RecommendationSettingsDto settings) =>
        new(RecommendationSettingsSaveStatus.Saved, settings, null);

    public static RecommendationSettingsSaveResult Invalid(string error) => new(RecommendationSettingsSaveStatus.Invalid, null, error);
}
