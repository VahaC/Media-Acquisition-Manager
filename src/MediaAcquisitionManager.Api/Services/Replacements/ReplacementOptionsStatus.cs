namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — how a read of what a release may replace went.</summary>
public enum ReplacementOptionsStatus
{
    /// <summary>The answer, a reason nothing can be replaced included.</summary>
    Listed = 1,

    /// <summary>A release or a destination wasn't named, or the holdings flag isn't true or false; the error says
    /// which.</summary>
    Invalid = 2,

    /// <summary>The release isn't one of the caller's searches' from the last day.</summary>
    ReleaseMissing = 3,

    /// <summary>The destination is gone.</summary>
    DestinationMissing = 4,
}
