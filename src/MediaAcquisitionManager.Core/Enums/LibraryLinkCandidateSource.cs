namespace MediaAcquisitionManager.Core.Enums;

/// <summary>MAM-34 — which query of a library work found a TMDb candidate first. Stored by name.</summary>
public enum LibraryLinkCandidateSource
{
    /// <summary>The work's display title, cleaned.</summary>
    Title = 1,

    /// <summary>The work's original title, cleaned.</summary>
    OriginalTitle = 2,

    /// <summary>The name of the folder its first file sits in, cleaned: a hint only (ADR 0017).</summary>
    FolderName = 3,
}
