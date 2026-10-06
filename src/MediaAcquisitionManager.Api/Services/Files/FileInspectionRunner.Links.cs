namespace MediaAcquisitionManager.Api.Services.Files;

// The owner's TMDb links after a run (MAM-34): the identities a run reads are what a link finds its item by again.
public sealed partial class FileInspectionRunner
{
    /// <summary>
    /// MAM-34 — hands what this run read to the links (<see cref="LibraryLinks.ILibraryLinkReattacher"/>): an item the
    /// library re-created under a new id has its identity only now, so its link finds it here rather than a sync later.
    /// </summary>
    /// <remarks>
    /// Best effort, the shape of the sync's own follow-ups: the run has succeeded and stays so, a failure here costs the
    /// readings nothing, and the next sync re-attaches what this pass couldn't. Only a shutdown propagates.
    /// </remarks>
    private async Task ReattachLinksAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await linkReattacher.ReattachAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "File inspection {RunId} succeeded, but the TMDb links could not be brought up to date after it.", runId);
        }
    }
}
