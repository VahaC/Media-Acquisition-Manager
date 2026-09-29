namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>How a connection test went.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Message">Always a readable sentence, success included ("Connected to Jellyfin
/// 12.1.0."), with the call's secrets masked.</param>
public sealed record ConnectionTestResult(IntegrationCallStatus Status, string Message)
{
    /// <summary>Whether the service answered the way its type does.</summary>
    public bool IsSuccess => Status == IntegrationCallStatus.Succeeded;

    /// <summary>A successful test and the sentence saying what answered.</summary>
    public static ConnectionTestResult Succeeded(string message) => new(IntegrationCallStatus.Succeeded, message);

    /// <summary>A failed test and the sentence saying why.</summary>
    public static ConnectionTestResult Failed(IntegrationCallStatus status, string message) => new(status, message);
}
