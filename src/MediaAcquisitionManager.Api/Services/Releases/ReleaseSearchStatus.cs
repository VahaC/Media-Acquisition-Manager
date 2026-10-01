namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>How a release search ended.</summary>
public enum ReleaseSearchStatus
{
    /// <summary>Every enabled source was asked (or said why it couldn't be); each answers for itself.</summary>
    Searched = 1,

    /// <summary>The title, the year or an id is not one the search takes, which the error names; no source
    /// was asked.</summary>
    Invalid = 2,
}
