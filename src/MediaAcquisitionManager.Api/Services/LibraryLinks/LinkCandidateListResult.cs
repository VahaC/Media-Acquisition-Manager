using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of a free TMDb search or a pasted id from a library work's dialog
/// (<see cref="ILibraryLinkSuggestionService"/>).</summary>
/// <param name="Status">How it went: <see cref="LibraryLinkStatus.Succeeded"/>, <see cref="LibraryLinkStatus.NotFound"/>
/// (no such work), <see cref="LibraryLinkStatus.Invalid"/>, <see cref="LibraryLinkStatus.CatalogWorkNotFound"/>,
/// <see cref="LibraryLinkStatus.Conflict"/> (no catalog) or <see cref="LibraryLinkStatus.BadGateway"/>.</param>
/// <param name="List">The candidates found, when it succeeded.</param>
/// <param name="Error">The sentence for the owner otherwise; <c>null</c> for a bare 404.</param>
public sealed record LinkCandidateListResult(LibraryLinkStatus Status, LinkCandidateListResponse? List, string? Error)
{
    public static LinkCandidateListResult Listed(LinkCandidateListResponse list) => new(LibraryLinkStatus.Succeeded, list, null);

    public static LinkCandidateListResult NotFound { get; } = new(LibraryLinkStatus.NotFound, null, null);

    public static LinkCandidateListResult Invalid(string error) => new(LibraryLinkStatus.Invalid, null, error);

    public static LinkCandidateListResult CatalogWorkNotFound(string error) => new(LibraryLinkStatus.CatalogWorkNotFound, null, error);

    public static LinkCandidateListResult Conflict(string error) => new(LibraryLinkStatus.Conflict, null, error);

    public static LinkCandidateListResult BadGateway(string error) => new(LibraryLinkStatus.BadGateway, null, error);
}
