namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — the outcome of <see cref="ILibraryLinkWriter.WriteAsync"/>.</summary>
/// <param name="Status">Where it left the link.</param>
/// <param name="Error">The sentence stored on the link as its error, when there is one.</param>
public sealed record LibraryLinkWriteResult(LibraryLinkWriteStatus Status, string? Error = null)
{
    public static LibraryLinkWriteResult ClaimLost { get; } = new(LibraryLinkWriteStatus.ClaimLost);
}
