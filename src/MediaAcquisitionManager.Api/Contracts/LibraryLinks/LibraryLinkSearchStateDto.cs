namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — where the preparation of a library work's TMDb suggestions stands.</summary>
/// <param name="SearchedUtc">When the catalog was last searched for the work; <c>null</c> when never.</param>
/// <param name="NoneFitsUtc">When the owner said none of the suggestions fits; <c>null</c> unless they did.</param>
/// <param name="Pending">Whether a preparation is queued or running.</param>
/// <param name="InPopulation">Whether the work is one the preparation searches for: it lacks a TMDb id and has no
/// link.</param>
public sealed record LibraryLinkSearchStateDto(DateTime? SearchedUtc, DateTime? NoneFitsUtc, bool Pending, bool InPopulation);
