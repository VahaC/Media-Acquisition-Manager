using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of "Accept all confident" (<see cref="ILibraryLinkSuggestionService.AcceptConfidentAsync"/>).</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> (the click was taken: some works linked, some maybe
/// skipped), <see cref="LibraryLinkStatus.Invalid"/> or <see cref="LibraryLinkStatus.Conflict"/> (no catalog).</param>
/// <param name="Batch">What happened to each work, when taken.</param>
/// <param name="Error">Why not otherwise, as a message the Api renders (ADR 0042).</param>
public sealed record LibraryLinkAcceptResult(LibraryLinkStatus Status, LibraryLinkBatchResponse? Batch, Message? Error)
{
    public static LibraryLinkAcceptResult Accepted(LibraryLinkBatchResponse batch) => new(LibraryLinkStatus.Succeeded, batch, null);

    public static LibraryLinkAcceptResult Invalid(Message error) => new(LibraryLinkStatus.Invalid, null, error);

    public static LibraryLinkAcceptResult Conflict(Message error) => new(LibraryLinkStatus.Conflict, null, error);
}
