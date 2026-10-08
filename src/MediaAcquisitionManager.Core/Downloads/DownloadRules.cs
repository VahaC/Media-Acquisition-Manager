namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// What a request to send a release to a download client must name (MAM-13). Each rule answers one
/// readable sentence, or <c>null</c> when the value is acceptable — the MAM-4 rules-class pattern of
/// <see cref="DestinationRules"/>.
/// </summary>
public static class DownloadRules
{
    private const string RequestIdMissingError =
        "The download has no request id, which keeps a retry from sending it twice. Reload the page and try again.";

    private const string ReleaseMissingError = "Pick the release to send.";
    private const string ClientMissingError = "Pick the download client to send it to.";
    private const string DestinationMissingError = "Pick the destination to send it to.";

    /// <summary>
    /// The sentence saying what a send request is missing, or <c>null</c>. The request id comes first: a
    /// request without one couldn't be told from its own retry, so nothing else of it is worth reading.
    /// </summary>
    public static string? Validate(Guid? requestId, Guid? releaseId, Guid? downloadClientId, Guid? destinationId)
    {
        if (requestId is null || requestId == Guid.Empty)
            return RequestIdMissingError;
        if (ValidateReleaseId(releaseId) is { } releaseError)
            return releaseError;
        if (downloadClientId is null || downloadClientId == Guid.Empty)
            return ClientMissingError;
        if (destinationId is null || destinationId == Guid.Empty)
            return DestinationMissingError;
        return null;
    }

    /// <summary>The sentence saying the release is missing, or <c>null</c>.</summary>
    public static string? ValidateReleaseId(Guid? releaseId) =>
        releaseId is null || releaseId == Guid.Empty ? ReleaseMissingError : null;

    /// <summary>MAM-38 — the destination the Download dialog asks about: present, or the sentence why not.</summary>
    public static string? ValidateDestinationId(Guid? destinationId) =>
        destinationId is null || destinationId == Guid.Empty ? DestinationMissingError : null;
}
