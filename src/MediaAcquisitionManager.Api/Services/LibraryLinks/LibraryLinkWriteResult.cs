using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of <see cref="ILibraryLinkWriter.WriteAsync"/>.</summary>
/// <param name="Status">Where it left the link.</param>
/// <param name="Error">The message stored on the link as its error (ADR 0042), when there is one.</param>
/// <param name="Outcome">How the library decided the identification, when it was asked and answered; <c>null</c> when the
/// link was kept in the app, taken over meanwhile or the app failed before asking. The background writer reads it for its
/// circuit breaker: only <see cref="LibraryIdentifyOutcome.NotConfirmed"/> counts against a batch.</param>
public sealed record LibraryLinkWriteResult(LibraryLinkWriteStatus Status, Message? Error = null, LibraryIdentifyOutcome? Outcome = null)
{
    public static LibraryLinkWriteResult ClaimLost { get; } = new(LibraryLinkWriteStatus.ClaimLost);
}
