namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — an image a source served, whole.</summary>
/// <param name="Content">Its bytes.</param>
/// <param name="MediaType">Its media type, <c>image/jpeg</c> or <c>image/png</c>: the only ones kept, so the
/// app never serves a type it didn't check.</param>
public sealed record CatalogImage(byte[] Content, string MediaType);
