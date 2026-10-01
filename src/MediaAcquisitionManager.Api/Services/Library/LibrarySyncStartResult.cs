using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>The outcome of <see cref="ILibrarySyncService.StartAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="LibrarySyncStartStatus.Queued"/>.</param>
/// <param name="Error">The sentence for the owner when it wasn't queued and there is a reason to give.</param>
public sealed record LibrarySyncStartResult(LibrarySyncStartStatus Status, LibrarySyncRunDto? Run, string? Error)
{
    public static LibrarySyncStartResult NotFound { get; } = new(LibrarySyncStartStatus.NotFound, null, null);

    public static LibrarySyncStartResult Queued(LibrarySyncRunDto run) => new(LibrarySyncStartStatus.Queued, run, null);

    public static LibrarySyncStartResult Conflict(string error) => new(LibrarySyncStartStatus.Conflict, null, error);

    public static LibrarySyncStartResult Unavailable(string error) => new(LibrarySyncStartStatus.Unavailable, null, error);
}
