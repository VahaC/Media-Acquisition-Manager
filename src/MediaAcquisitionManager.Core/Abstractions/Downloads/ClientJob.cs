namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>One job of a download client, as the client reports it.</summary>
/// <param name="ClientJobId">The client's id of the job.</param>
/// <param name="Name">Its name.</param>
/// <param name="State">Its state.</param>
/// <param name="Progress">From 0 to 1; <c>null</c> when unknown.</param>
/// <param name="DestinationKey">The client destination it is in; <c>null</c> when none.</param>
/// <param name="ServiceContentPath">Where its content is, as the client sees it.</param>
public sealed record ClientJob(
    string ClientJobId,
    string Name,
    ClientJobState State,
    double? Progress,
    string? DestinationKey,
    string? ServiceContentPath);
