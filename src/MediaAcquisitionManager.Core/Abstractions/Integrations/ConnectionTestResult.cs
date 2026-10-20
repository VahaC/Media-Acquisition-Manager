using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>How a connection test went.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Message">Always a message the Api renders (ADR 0042), success included ("Connected to Jellyfin
/// 12.1.0."), with the call's secrets masked.</param>
public sealed record ConnectionTestResult(IntegrationCallStatus Status, Message Message)
{
    /// <summary>Whether the service answered the way its type does.</summary>
    public bool IsSuccess => Status == IntegrationCallStatus.Succeeded;

    /// <summary>A successful test and the message saying what answered.</summary>
    public static ConnectionTestResult Succeeded(Message message) => new(IntegrationCallStatus.Succeeded, message);

    /// <summary>A failed test and the message saying why.</summary>
    public static ConnectionTestResult Failed(IntegrationCallStatus status, Message message) => new(status, message);
}
