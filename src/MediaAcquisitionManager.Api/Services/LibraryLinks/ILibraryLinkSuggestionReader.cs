using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — the TMDb suggestions a library work's dialog shows, and where their preparation stands: the seam between
/// the dialog (<see cref="ILibraryLinkService.ReadDialogAsync"/>) and the suggestions the background preparation stored.
/// </summary>
/// <remarks>
/// Read, never fetched: the evidence, the order and "confident" are worked out from the stored candidates when the
/// dialog asks (decision 7), so opening a dialog never calls the catalog, and a duration measured later, or a neighbour
/// linked later, changes them without searching again.
/// </remarks>
public interface ILibraryLinkSuggestionReader
{
    /// <summary>The suggestions of the work <paramref name="workId"/>, best first, and its preparation's state.</summary>
    Task<LibraryLinkSuggestions> ReadAsync(Guid workId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The confident suggestion (decision 8) of each of <paramref name="workIds"/> that has one, from its stored
    /// candidates as they rank now — the TMDb page's confident list, and "Accept all confident" checking a click again.
    /// A work with none, or gone, is left out.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, LinkCandidateDto>> ReadConfidentAsync(IReadOnlyCollection<Guid> workIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="candidates"/> — films and shows the catalog cache holds, in the catalog's order — ranked for the
    /// work <paramref name="workId"/> with their evidence, as its suggestions are: a free search's or a pasted id's
    /// answer. <paramref name="seasonNumber"/> and <paramref name="episodeNumber"/> are the numbers a pasted TMDb episode
    /// link named, prefilled for a show in place of what the work's names say. A candidate the cache doesn't hold is left
    /// out; none when the work is gone.
    /// </summary>
    Task<IReadOnlyList<LinkCandidateDto>> RankAsync(
        Guid workId,
        IReadOnlyList<LinkCandidateKey> candidates,
        int? seasonNumber = null,
        int? episodeNumber = null,
        CancellationToken cancellationToken = default);
}
