namespace MediaAcquisitionManager.Core.Enums;

/// <summary>Which of a work's titles a source gave. Stored by name.</summary>
public enum MediaTitleKind
{
    /// <summary>
    /// The title the source shows, often a localized one ("Чужі"). Never assumed to be localized or
    /// not: the source only says it is the one it displays.
    /// </summary>
    Display = 1,

    /// <summary>The title in the work's original language, as the source gives it ("Aliens").</summary>
    Original = 2,
}
