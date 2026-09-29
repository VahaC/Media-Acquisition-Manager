namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>One category of a search source.</summary>
/// <param name="Id">The source's id of the category.</param>
/// <param name="Name">Its name as the source gives it.</param>
/// <param name="ParentId">The category it belongs under; <c>null</c> for a top-level one.</param>
public sealed record ReleaseCategory(string Id, string Name, string? ParentId);
