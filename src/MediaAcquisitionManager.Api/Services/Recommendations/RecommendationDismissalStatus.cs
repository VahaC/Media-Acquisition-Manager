namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — how marking a work "Not interested", or undoing the mark, ended.</summary>
public enum RecommendationDismissalStatus
{
    /// <summary>The work is now marked, or now unmarked, for the caller — whether or not it was already.</summary>
    Done = 1,

    /// <summary>The kind or the catalog id is not one a mark takes, which the error names; nothing changed.</summary>
    Invalid = 2,
}
