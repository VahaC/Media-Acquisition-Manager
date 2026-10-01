namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>How making a stored search ended (MAM-12).</summary>
public enum SearchCreateStatus
{
    /// <summary>The search is stored: the library was read and every enabled source was asked (or said why
    /// it couldn't be), each answering for itself.</summary>
    Created = 1,

    /// <summary>The title, the year or an id is not one the search takes, which the error names; nothing
    /// was stored and no source was asked.</summary>
    Invalid = 2,
}
