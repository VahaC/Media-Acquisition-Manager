namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — one new video file of a replacement's download that passed the client's word and the app's own look
/// (<see cref="Entities.ReplacementJobNewFileEntity"/>), with what the library says of it now (ADR 0035).
/// </summary>
/// <param name="Id">Its row's id.</param>
/// <param name="Position">Its place among the download's files, in the client's order.</param>
/// <param name="AppPath">Its path inside the app, canonical: what the sentences quote, and whose name is read.</param>
/// <param name="Library">What the library says of its item.</param>
/// <param name="DurationMilliseconds">Its measured running time.</param>
public sealed record ReplacementPairingNewFile(
    Guid Id,
    int Position,
    string AppPath,
    ReplacementLibraryReading Library,
    long? DurationMilliseconds);
