using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of removing a work's TMDb links (<see cref="ILibraryLinkService.RemoveAsync"/>): the
/// response is built here, never in the controller (house rule: a service or a mapper builds contracts).</summary>
/// <param name="Status">How it went.</param>
/// <param name="Response">What the work's dialog shows afterwards, when <see cref="LibraryLinkStatus.Succeeded"/>.</param>
/// <param name="Error">The sentence for the owner otherwise; <c>null</c> for a bare 404 and for 204.</param>
public sealed record LibraryLinkRemoveResult(LibraryLinkStatus Status, LibraryLinkRemoveResponse? Response, string? Error)
{
    public static LibraryLinkRemoveResult Removed(LibraryLinkDialogResponse dialog) =>
        new(LibraryLinkStatus.Succeeded, new LibraryLinkRemoveResponse(dialog), null);

    /// <summary>Anything but a removal: <paramref name="result"/>'s status and sentence.</summary>
    public static LibraryLinkRemoveResult Not(LibraryLinkResult result) => new(result.Status, null, result.Error);
}
