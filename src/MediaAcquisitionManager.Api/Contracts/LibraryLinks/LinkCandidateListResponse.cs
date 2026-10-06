namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the TMDb films and shows a free search or a pasted id found for a library work, each with its
/// evidence.</summary>
/// <param name="Items">The candidates, best first: the first page of a search (the year narrows it), or the one a pasted
/// id names — several when an IMDb id names a film and a show.</param>
public sealed record LinkCandidateListResponse(IReadOnlyList<LinkCandidateDto> Items);
