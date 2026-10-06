using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the review queue at an index (<see cref="ILibraryLinkSuggestionService.ReadQueueAsync"/>), or why
/// not.</summary>
/// <param name="Status"><see cref="LibraryLinkStatus.Succeeded"/> or <see cref="LibraryLinkStatus.Invalid"/>.</param>
/// <param name="Queue">The queue when read.</param>
/// <param name="Error">The sentence for an index the queue doesn't take.</param>
public sealed record LibraryLinkQueueResult(LibraryLinkStatus Status, LibraryLinkQueueResponse? Queue, string? Error)
{
    public static LibraryLinkQueueResult Read(LibraryLinkQueueResponse queue) => new(LibraryLinkStatus.Succeeded, queue, null);

    public static LibraryLinkQueueResult Invalid(string error) => new(LibraryLinkStatus.Invalid, null, error);
}
