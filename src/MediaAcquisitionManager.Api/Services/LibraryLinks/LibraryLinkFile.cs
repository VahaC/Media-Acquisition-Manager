namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>MAM-34 — one file of a library work, as the TMDb suggestions read it (<see cref="LibraryLinkFiles"/>).</summary>
/// <param name="WorkId">The work asked about: the film or episode the file is of, or the series whose episode it is.</param>
/// <param name="VersionWorkId">The work whose version holds the file: <paramref name="WorkId"/> itself, or the series'
/// episode.</param>
/// <param name="VersionId">The version the file is (a part) of.</param>
/// <param name="ServicePath">The path the library reported.</param>
/// <param name="LibraryLocationId">The library that holds it; <c>null</c> when none or several do.</param>
/// <param name="DurationMilliseconds">How long the app measured it to be (ffprobe); <c>null</c> unless the file's last look
/// read it.</param>
public sealed record LibraryLinkFile(
    Guid WorkId,
    Guid VersionWorkId,
    Guid VersionId,
    string ServicePath,
    Guid? LibraryLocationId,
    long? DurationMilliseconds);
