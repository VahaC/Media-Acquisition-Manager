namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — a library work and its confident TMDb suggestion.</summary>
/// <param name="Work">The work as the Library page shows it.</param>
/// <param name="Candidate">The suggestion, with its evidence.</param>
public sealed record ConfidentSuggestionDto(LibraryWorkDto Work, LinkCandidateDto Candidate);
