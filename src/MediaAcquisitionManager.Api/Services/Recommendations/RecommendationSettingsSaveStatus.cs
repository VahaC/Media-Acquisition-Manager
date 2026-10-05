namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — how saving a user's recommendation settings ended.</summary>
public enum RecommendationSettingsSaveStatus
{
    /// <summary>The settings were saved.</summary>
    Saved = 1,

    /// <summary>A field is missing or out of range, which the error names; nothing was saved.</summary>
    Invalid = 2,
}
