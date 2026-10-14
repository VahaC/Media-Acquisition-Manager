namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>
/// What a film or a show is about, in the one language it was asked for (<see cref="IMetadataCatalog.GetOverviewAsync"/>).
/// </summary>
/// <param name="Text">The overview; <c>null</c> when the source has none in that language.</param>
public sealed record CatalogOverview(string? Text);
