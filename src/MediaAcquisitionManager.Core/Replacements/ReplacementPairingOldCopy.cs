namespace MediaAcquisitionManager.Core.Replacements;

/// <summary>
/// MAM-25 — one old copy the owner ticked for a replacement (<see cref="Entities.ReplacementJobFileEntity"/>), with what
/// the verification read of it (ADR 0035).
/// </summary>
/// <param name="Id">Its row's id.</param>
/// <param name="Position">Its place in the job's list.</param>
/// <param name="MatchedWorkId">The matched work it is a copy of: the film, or the episode's series.</param>
/// <param name="SeasonNumber">An episode's season when it was scheduled; <c>null</c> for a film.</param>
/// <param name="EpisodeNumber">An episode's number then; <c>null</c> for a film, and for an episode the library numbered
/// none.</param>
/// <param name="EpisodeTitles">An episode's own titles, which are no edition label of its new file's name either — its
/// show's are its matched work's (MAM-37's <c>CopyEditions.TitlesOfWork</c>); empty for a film.</param>
/// <param name="AppPath">Its path inside the app, canonical: what the sentences quote, and whose name is read.</param>
/// <param name="Library">What the library says of an episode's item now; <c>null</c> for a film, whose item isn't
/// read.</param>
/// <param name="DurationMilliseconds">Its measured running time; <c>null</c> when it was never measured.</param>
public sealed record ReplacementPairingOldCopy(
    Guid Id,
    int Position,
    Guid MatchedWorkId,
    int? SeasonNumber,
    int? EpisodeNumber,
    IReadOnlyList<string> EpisodeTitles,
    string AppPath,
    ReplacementLibraryReading? Library,
    long? DurationMilliseconds);
