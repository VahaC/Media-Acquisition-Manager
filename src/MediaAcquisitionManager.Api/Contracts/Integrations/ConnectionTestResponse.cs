using MediaAcquisitionManager.Core.Abstractions.Integrations;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>What a connection test found, whatever the service answered (ADR 0005).</summary>
/// <param name="Status">The outcome; the dialog colours the sentence by it.</param>
/// <param name="Message">One readable sentence saying what happened, with every secret masked.</param>
public sealed record ConnectionTestResponse(IntegrationCallStatus Status, string Message);
