namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — runs one queued preparation of the TMDb suggestions: claims it, reads the catalog's genres and a sample of
/// each library's identified works (so what a library mostly holds is known), then searches the catalog for each library
/// work without a TMDb id — by its cleaned titles and its folder's name, with its year and without — and stores the
/// films and shows found, each read through the catalog cache, as the work's candidates.
/// </summary>
/// <remarks>
/// Only the catalog's answers are stored: the evidence, the order and "confident" are worked out when the suggestions are
/// read (decision 7). A work searched within <c>CatalogStore.RefreshAge</c> by the same queries keeps its candidates —
/// unless the owner asked for the run, which searches every work again (R21). Each work is saved as soon as it is
/// searched, so a run the catalog fails keeps the works it finished. Nothing is ever written to the library's tables.
/// </remarks>
public interface ILibraryLinkSuggestionRunner
{
    /// <summary>
    /// Runs the queued run <paramref name="runId"/>; does nothing when it is no longer queued (another worker took it, or
    /// the reconciler failed it). Only a cancellation by the caller (the app shutting down) propagates, leaving the run
    /// running for the reconciler.
    /// </summary>
    Task RunAsync(Guid runId, CancellationToken cancellationToken = default);
}
