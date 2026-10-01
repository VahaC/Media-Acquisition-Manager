namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Runs the media inspector over one file and hands back what it printed (MAM-9). The seam between the
/// inventory's checks and the ffprobe process, so tests drive the inventory with a stub executable and a
/// short time limit.
/// </summary>
internal interface IMediaProbeProcess
{
    /// <summary>
    /// The inspector's JSON for the file at <paramref name="probePath"/>, or why there is none. Never throws on
    /// an expected failure; a cancellation by the caller stops the inspector first and then propagates as
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="probePath">The path that leads the inspector to the file the inventory checked and opened
    /// (<see cref="IOpenedFile.ProbePath"/>).</param>
    /// <param name="cancellationToken">Cancels the run, stopping the inspector.</param>
    Task<MediaProbeResult> RunAsync(string probePath, CancellationToken cancellationToken = default);
}
