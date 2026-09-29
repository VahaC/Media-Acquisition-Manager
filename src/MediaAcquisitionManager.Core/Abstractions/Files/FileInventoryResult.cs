namespace MediaAcquisitionManager.Core.Abstractions.Files;

/// <summary>
/// The result of a look at a local file. Its own status rather than
/// <see cref="Integrations.IntegrationCallStatus"/>: nothing here is a call to a service.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Value">What was read; <c>null</c> unless the file was found.</param>
/// <param name="Error">A readable sentence; <c>null</c> when the file was found.</param>
public sealed record FileInventoryResult<T>(FileInventoryStatus Status, T? Value, string? Error) where T : class;
