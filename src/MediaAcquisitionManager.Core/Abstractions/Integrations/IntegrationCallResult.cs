namespace MediaAcquisitionManager.Core.Abstractions.Integrations;

/// <summary>The result of every kind-contract call: a value on success, a sentence otherwise.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="Value">What the service answered; <c>null</c> unless the call succeeded.</param>
/// <param name="Error">A readable sentence with the call's secrets masked; <c>null</c> on success.</param>
public sealed record IntegrationCallResult<T>(IntegrationCallStatus Status, T? Value, string? Error) where T : class
{
    /// <summary>Whether the call succeeded.</summary>
    public bool IsSuccess => Status == IntegrationCallStatus.Succeeded;

    /// <summary>A successful call and its value.</summary>
    public static IntegrationCallResult<T> Succeeded(T value) => new(IntegrationCallStatus.Succeeded, value, null);

    /// <summary>A failed call and the sentence saying why.</summary>
    public static IntegrationCallResult<T> Failed(IntegrationCallStatus status, string error) => new(status, null, error);
}
