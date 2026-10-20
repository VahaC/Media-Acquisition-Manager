using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the review queue at an index (<see cref="ILibraryLinkSuggestionService.ReadQueueAsync"/>), or why
/// not.</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> or <see cref="LibraryLinkStatus.Invalid"/>.</param>
/// <param name="Queue">The queue when read.</param>
/// <param name="Error">Why an index isn't taken, as a message the Api renders (ADR 0042).</param>
public sealed record LibraryLinkQueueResult(LibraryLinkStatus Status, LibraryLinkQueueResponse? Queue, Message? Error)
{
    public static LibraryLinkQueueResult Read(LibraryLinkQueueResponse queue) => new(LibraryLinkStatus.Succeeded, queue, null);

    public static LibraryLinkQueueResult Invalid(Message error) => new(LibraryLinkStatus.Invalid, null, error);
}
