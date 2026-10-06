namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// MAM-34 — whether a TMDb candidate's genres agree with what the library its work sits in mostly holds (animation,
/// documentaries).
/// </summary>
public enum LinkGenreAgreement
{
    /// <summary>The candidate has the library's genre.</summary>
    Agrees = 1,

    /// <summary>The candidate lacks the library's genre.</summary>
    Disagrees = 2,

    /// <summary>The library has no genre of its own, or isn't known: nothing to compare.</summary>
    Unknown = 3,
}
