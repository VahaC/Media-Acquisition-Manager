using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of a read or a change of a library work's TMDb links (<see cref="ILibraryLinkService"/>).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Dialog">The work's dialog as it stands afterwards, when <see cref="LibraryLinkStatus.Succeeded"/>; none for a
/// link removed by its id.</param>
/// <param name="Error">The sentence for the owner otherwise; <c>null</c> for a bare 404 and for 204.</param>
public sealed record LibraryLinkResult(LibraryLinkStatus Status, LibraryLinkDialogResponse? Dialog, string? Error)
{
    public static LibraryLinkResult Succeeded(LibraryLinkDialogResponse? dialog) => new(LibraryLinkStatus.Succeeded, dialog, null);

    public static LibraryLinkResult NotFound { get; } = new(LibraryLinkStatus.NotFound, null, null);

    public static LibraryLinkResult NothingToRemove { get; } = new(LibraryLinkStatus.NothingToRemove, null, null);

    public static LibraryLinkResult Invalid(string error) => new(LibraryLinkStatus.Invalid, null, error);

    public static LibraryLinkResult CatalogWorkNotFound(string error) => new(LibraryLinkStatus.CatalogWorkNotFound, null, error);

    public static LibraryLinkResult Conflict(string error) => new(LibraryLinkStatus.Conflict, null, error);

    public static LibraryLinkResult BadGateway(string error) => new(LibraryLinkStatus.BadGateway, null, error);
}
