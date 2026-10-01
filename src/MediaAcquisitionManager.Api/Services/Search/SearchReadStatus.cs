namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>How reading a stored search ended (MAM-12).</summary>
public enum SearchReadStatus
{
    /// <summary>The search was read.</summary>
    Found = 1,

    /// <summary>No search of the caller's has that id: none has, it is another user's, or it is more than a
    /// day old. One answer for all three, so nobody learns another user's search exists.</summary>
    NotFound = 2,
}
