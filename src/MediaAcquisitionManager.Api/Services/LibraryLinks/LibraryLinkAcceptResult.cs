using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of "Accept all confident" (<see cref="ILibraryLinkSuggestionService.AcceptConfidentAsync"/>).</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> (the click was taken: some works linked, some maybe
/// skipped), <see cref="LibraryLinkStatus.Invalid"/> or <see cref="LibraryLinkStatus.Conflict"/> (no catalog).</param>
/// <param name="Batch">What happened to each work, when taken.</param>
/// <param name="Error">The sentence otherwise.</param>
public sealed record LibraryLinkAcceptResult(LibraryLinkStatus Status, LibraryLinkBatchResponse? Batch, string? Error)
{
    public static LibraryLinkAcceptResult Accepted(LibraryLinkBatchResponse batch) => new(LibraryLinkStatus.Succeeded, batch, null);

    public static LibraryLinkAcceptResult Invalid(string error) => new(LibraryLinkStatus.Invalid, null, error);

    public static LibraryLinkAcceptResult Conflict(string error) => new(LibraryLinkStatus.Conflict, null, error);
}
