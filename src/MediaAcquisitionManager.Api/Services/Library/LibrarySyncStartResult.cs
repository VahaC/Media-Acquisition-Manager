using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>The outcome of <see cref="ILibrarySyncService.StartAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Run">The queued run when <see cref="LibrarySyncStartStatus.Queued"/>.</param>
/// <param name="Error">Why it wasn't queued, for the owner, as a message the Api renders (ADR 0042), when there is a
/// reason to give.</param>
public sealed record LibrarySyncStartResult(LibrarySyncStartStatus Status, LibrarySyncRunDto? Run, Message? Error)
{
    public static LibrarySyncStartResult NotFound { get; } = new(LibrarySyncStartStatus.NotFound, null, null);

    public static LibrarySyncStartResult Queued(LibrarySyncRunDto run) => new(LibrarySyncStartStatus.Queued, run, null);

    public static LibrarySyncStartResult Conflict(Message error) => new(LibrarySyncStartStatus.Conflict, null, error);

    public static LibrarySyncStartResult Unavailable(Message error) => new(LibrarySyncStartStatus.Unavailable, null, error);
}
