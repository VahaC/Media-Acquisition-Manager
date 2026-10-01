namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A source's category a release is listed in.</summary>
/// <param name="Id">The source's id of it (<c>2040</c>).</param>
/// <param name="Name">Its name as the source's capabilities give it (<c>Movies/HD</c>); <c>null</c> when
/// they don't name it.</param>
public sealed record ReleaseCategoryDto(string Id, string? Name);
