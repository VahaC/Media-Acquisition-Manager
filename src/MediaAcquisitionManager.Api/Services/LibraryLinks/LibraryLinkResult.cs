using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of a read or a change of a library work's TMDb links (<see cref="ILibraryLinkService"/>).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Dialog">The work's dialog as it stands afterwards, when <see cref="LibraryLinkStatus.Succeeded"/>; none for a
/// link removed by its id.</param>
/// <param name="Error">Why not, for the owner, as a message the Api renders (ADR 0042); <c>null</c> for a bare 404 and for 204.</param>
public sealed record LibraryLinkResult(LibraryLinkStatus Status, LibraryLinkDialogResponse? Dialog, Message? Error)
{
    public static LibraryLinkResult Succeeded(LibraryLinkDialogResponse? dialog) => new(LibraryLinkStatus.Succeeded, dialog, null);

    public static LibraryLinkResult NotFound { get; } = new(LibraryLinkStatus.NotFound, null, null);

    public static LibraryLinkResult NothingToRemove { get; } = new(LibraryLinkStatus.NothingToRemove, null, null);

    public static LibraryLinkResult Invalid(Message error) => new(LibraryLinkStatus.Invalid, null, error);

    public static LibraryLinkResult CatalogWorkNotFound(Message error) => new(LibraryLinkStatus.CatalogWorkNotFound, null, error);

    public static LibraryLinkResult Conflict(Message error) => new(LibraryLinkStatus.Conflict, null, error);

    public static LibraryLinkResult BadGateway(Message error) => new(LibraryLinkStatus.BadGateway, null, error);
}
