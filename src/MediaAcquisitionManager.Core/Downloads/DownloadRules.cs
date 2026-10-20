using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Downloads;

/// <summary>
/// What a request to send a release to a download client must name (MAM-13). Each rule answers why a value is
/// refused, as a message the Api renders (ADR 0042), or <c>null</c> when the value is acceptable — the MAM-4
/// rules-class pattern of <see cref="DestinationRules"/>.
/// </summary>
public static class DownloadRules
{
    private static readonly Message RequestIdMissingError = Message.Of("downloads.request.request-id-missing");
    private static readonly Message ReleaseMissingError = Message.Of("downloads.request.release-missing");
    private static readonly Message ClientMissingError = Message.Of("downloads.request.client-missing");
    private static readonly Message DestinationMissingError = Message.Of("downloads.request.destination-missing");

    /// <summary>
    /// What a send request is missing, as a message the Api renders, or <c>null</c>. The request id comes first: a
    /// request without one couldn't be told from its own retry, so nothing else of it is worth reading.
    /// </summary>
    public static Message? Validate(Guid? requestId, Guid? releaseId, Guid? downloadClientId, Guid? destinationId)
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

    /// <summary>Why the release is missing, or <c>null</c>.</summary>
    public static Message? ValidateReleaseId(Guid? releaseId) =>
        releaseId is null || releaseId == Guid.Empty ? ReleaseMissingError : null;

    /// <summary>MAM-38 — the destination the Download dialog asks about: present, or the message why not.</summary>
    public static Message? ValidateDestinationId(Guid? destinationId) =>
        destinationId is null || destinationId == Guid.Empty ? DestinationMissingError : null;
}
