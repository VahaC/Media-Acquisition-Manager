using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Library;

/// <summary>MAM-35 — the outcome of <see cref="ILibraryPosterService.GetAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Content">The poster's bytes, a JPEG, when <see cref="LibraryPosterStatus.Served"/>.</param>
/// <param name="Error">Why, as a message the Api renders (ADR 0042), when <see cref="LibraryPosterStatus.Failed"/>.</param>
public sealed record LibraryPosterResult(LibraryPosterStatus Status, byte[]? Content, Message? Error)
{
    public static LibraryPosterResult NotFound { get; } = new(LibraryPosterStatus.NotFound, null, null);

    public static LibraryPosterResult Served(byte[] content) => new(LibraryPosterStatus.Served, content, null);

    public static LibraryPosterResult Failed(Message error) => new(LibraryPosterStatus.Failed, null, error);
}
