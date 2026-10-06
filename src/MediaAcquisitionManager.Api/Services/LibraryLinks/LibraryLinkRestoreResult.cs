using MediaAcquisitionManager.Core.Abstractions.Library;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of <see cref="ILibraryLinkWriter.RestoreAsync"/>.</summary>
/// <param name="Status">How it went.</param>
/// <param name="Write">What the library read and left, when <see cref="LibraryLinkRestoreStatus.Restored"/>: the
/// <c>LibraryIdsRestored</c> audit row's ids.</param>
/// <param name="Error">The sentence when <see cref="LibraryLinkRestoreStatus.Failed"/> or
/// <see cref="LibraryLinkRestoreStatus.SwitchedOff"/>, the library's secrets masked.</param>
public sealed record LibraryLinkRestoreResult(LibraryLinkRestoreStatus Status, LibraryIdentifierWrite? Write = null, string? Error = null)
{
    public static LibraryLinkRestoreResult NotNeeded { get; } = new(LibraryLinkRestoreStatus.NotNeeded);

    public static LibraryLinkRestoreResult ItemGone { get; } = new(LibraryLinkRestoreStatus.ItemGone);

    public static LibraryLinkRestoreResult Restored(LibraryIdentifierWrite write) => new(LibraryLinkRestoreStatus.Restored, write);

    public static LibraryLinkRestoreResult Failed(string error) => new(LibraryLinkRestoreStatus.Failed, null, error);

    public static LibraryLinkRestoreResult SwitchedOff(string error) => new(LibraryLinkRestoreStatus.SwitchedOff, null, error);
}
