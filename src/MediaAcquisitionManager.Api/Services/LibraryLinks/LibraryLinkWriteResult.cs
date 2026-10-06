using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of <see cref="ILibraryLinkWriter.WriteAsync"/>.</summary>
/// <param name="Status">Where it left the link.</param>
/// <param name="Error">The sentence stored on the link as its error, when there is one.</param>
/// <param name="Outcome">How the library decided the identification, when it was asked and answered; <c>null</c> when the
/// link was kept in the app, taken over meanwhile or the app failed before asking. The background writer reads it for its
/// circuit breaker: only <see cref="LibraryIdentifyOutcome.NotConfirmed"/> counts against a batch.</param>
public sealed record LibraryLinkWriteResult(LibraryLinkWriteStatus Status, string? Error = null, LibraryIdentifyOutcome? Outcome = null)
{
    public static LibraryLinkWriteResult ClaimLost { get; } = new(LibraryLinkWriteStatus.ClaimLost);
}
