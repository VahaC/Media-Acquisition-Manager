namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>Every type of service this build can connect to.</summary>
/// <remarks>Fixed by the build and a handful of rows, so never paged.</remarks>
/// <param name="Items">The types, ordered by kind, then by name in any letter case.</param>
public sealed record IntegrationAdapterTypeListResponse(IReadOnlyList<IntegrationAdapterTypeDto> Items);
