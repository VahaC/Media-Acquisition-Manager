using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of a free TMDb search or a pasted id from a library work's dialog
/// (<see cref="ILibraryLinkSuggestionService"/>).</summary>
/// <param name="Status">How it went: <see cref="LibraryLinkStatus.Succeeded"/>, <see cref="LibraryLinkStatus.NotFound"/>
/// (no such work), <see cref="LibraryLinkStatus.Invalid"/>, <see cref="LibraryLinkStatus.CatalogWorkNotFound"/>,
/// <see cref="LibraryLinkStatus.Conflict"/> (no catalog) or <see cref="LibraryLinkStatus.BadGateway"/>.</param>
/// <param name="List">The candidates found, when it succeeded.</param>
/// <param name="Error">Why not, for the owner, as a message the Api renders (ADR 0042); <c>null</c> for a bare 404.</param>
public sealed record LinkCandidateListResult(LibraryLinkStatus Status, LinkCandidateListResponse? List, Message? Error)
{
    public static LinkCandidateListResult Listed(LinkCandidateListResponse list) => new(LibraryLinkStatus.Succeeded, list, null);

    public static LinkCandidateListResult NotFound { get; } = new(LibraryLinkStatus.NotFound, null, null);

    public static LinkCandidateListResult Invalid(Message error) => new(LibraryLinkStatus.Invalid, null, error);

    public static LinkCandidateListResult CatalogWorkNotFound(Message error) => new(LibraryLinkStatus.CatalogWorkNotFound, null, error);

    public static LinkCandidateListResult Conflict(Message error) => new(LibraryLinkStatus.Conflict, null, error);

    public static LinkCandidateListResult BadGateway(Message error) => new(LibraryLinkStatus.BadGateway, null, error);
}
