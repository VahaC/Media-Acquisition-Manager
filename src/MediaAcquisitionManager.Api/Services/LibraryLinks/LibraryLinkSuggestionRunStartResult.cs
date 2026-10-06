using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of <see cref="ILibraryLinkSuggestionRunService.StartAsync"/> and
/// <see cref="ILibraryLinkSuggestionRunService.EnsureQueuedAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="LibraryLinkSuggestionRunStartStatus.Queued"/>.</param>
/// <param name="Error">The sentence when it wasn't queued.</param>
public sealed record LibraryLinkSuggestionRunStartResult(LibraryLinkSuggestionRunStartStatus Status, LibraryLinkSuggestionRunDto? Run, string? Error)
{
    public static LibraryLinkSuggestionRunStartResult Queued(LibraryLinkSuggestionRunDto run) => new(LibraryLinkSuggestionRunStartStatus.Queued, run, null);

    public static LibraryLinkSuggestionRunStartResult NoCatalog(string error) => new(LibraryLinkSuggestionRunStartStatus.NoCatalog, null, error);

    public static LibraryLinkSuggestionRunStartResult Unavailable(string error) => new(LibraryLinkSuggestionRunStartStatus.Unavailable, null, error);
}
