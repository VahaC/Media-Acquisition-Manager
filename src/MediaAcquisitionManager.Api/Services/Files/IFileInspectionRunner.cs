namespace MediaAcquisitionManager.Api.Services.Files;

/// <summary>
/// Runs one queued inspection of the library's files (MAM-9): claims it, looks at every file name the
/// libraries reported through the file inventory, inside the allowed roots only, reads with the media
/// inspector what the cache doesn't already hold, and writes what each file showed as it goes.
/// </summary>
/// <remarks>
/// A file that can't be read is a result of the run, written on the file with its sentence — never a
/// failure of the run. The run fails only for what stops it as a whole (no folder allowed any more, a
/// bug); what it had written stays.
/// </remarks>
public interface IFileInspectionRunner
{
    /// <summary>
    /// Runs the queued run <paramref name="runId"/>; does nothing when it is no longer queued (another
    /// worker took it, or the reconciler failed it). Only a cancellation by the caller (the app shutting
    /// down) propagates, leaving the run running for the reconciler.
    /// </summary>
    Task RunAsync(Guid runId, CancellationToken cancellationToken = default);
}
