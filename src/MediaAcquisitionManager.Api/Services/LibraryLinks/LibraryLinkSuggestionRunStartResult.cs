using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of <see cref="ILibraryLinkSuggestionRunService.StartAsync"/> and
/// <see cref="ILibraryLinkSuggestionRunService.EnsureQueuedAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="LibraryLinkSuggestionRunStartStatus.Queued"/>.</param>
/// <param name="Error">Why it wasn't queued, as a message the Api renders (ADR 0042).</param>
public sealed record LibraryLinkSuggestionRunStartResult(LibraryLinkSuggestionRunStartStatus Status, LibraryLinkSuggestionRunDto? Run, Message? Error)
{
    public static LibraryLinkSuggestionRunStartResult Queued(LibraryLinkSuggestionRunDto run) => new(LibraryLinkSuggestionRunStartStatus.Queued, run, null);

    public static LibraryLinkSuggestionRunStartResult NoCatalog(Message error) => new(LibraryLinkSuggestionRunStartStatus.NoCatalog, null, error);

    public static LibraryLinkSuggestionRunStartResult Unavailable(Message error) => new(LibraryLinkSuggestionRunStartStatus.Unavailable, null, error);
}
