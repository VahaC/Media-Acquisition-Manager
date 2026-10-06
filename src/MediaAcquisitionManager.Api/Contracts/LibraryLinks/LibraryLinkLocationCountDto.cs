namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — one library's numbers on the TMDb page.</summary>
/// <param name="LocationId">The library; <c>null</c> for the works in no known library.</param>
/// <param name="LocationName">Its name.</param>
/// <param name="PopulationCount">Its works without a TMDb id and without a link.</param>
/// <param name="ConfidentCount">How many of them have a confident suggestion.</param>
public sealed record LibraryLinkLocationCountDto(Guid? LocationId, string? LocationName, int PopulationCount, int ConfidentCount);
