using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <summary>
/// MAM-34 — finding a library work's TMDb film or show besides its prepared suggestions, and the TMDb page's review: a
/// free search and a pasted id from the dialog, the review queue of the works without an id with the owner's "none
/// fits", the confident suggestions with each library's numbers, and "Accept all confident".
/// </summary>
/// <remarks>
/// <para>
/// Every candidate shown is ranked the way the suggestions are (<see cref="ILibraryLinkSuggestionReader"/>), its evidence
/// read from the catalog cache, and every film or show the catalog is asked about is stored there first — so its poster
/// serves and its facts are rows. Expected failures come back as result statuses, never as exceptions.
/// </para>
/// <para>
/// Owner-only, every member (<c>LibraryLinksAreOwnerOnlyConvention</c>): these call the owner's catalog and change the
/// owner's library.
/// </para>
/// </remarks>
public interface ILibraryLinkSuggestionService
{
    /// <summary>
    /// The catalog's first page of films and shows titled <paramref name="title"/> — of <paramref name="year"/> when one
    /// is given — of the kinds the work <paramref name="workId"/> could be linked to, the ones kept ranked for it.
    /// </summary>
    Task<LinkCandidateListResult> SearchAsync(Guid workId, string? title, string? year, CancellationToken cancellationToken = default);

    /// <summary>
    /// The film or show a pasted <paramref name="text"/> names — a TMDb or IMDb link, an IMDb id, or a bare TMDb number in
    /// the numbering <paramref name="kind"/> says (ADR 0017) —, ranked for the work <paramref name="workId"/>; several
    /// when an IMDb id names a film and a show.
    /// </summary>
    Task<LinkCandidateListResult> LookupAsync(Guid workId, string? text, string? kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// The review queue — the works without a TMDb id, a link or the owner's "none fits", in the Library page's title order
    /// — with the one at <paramref name="index"/> (from 0; blank for the first) as its dialog.
    /// </summary>
    Task<LibraryLinkQueueResult> ReadQueueAsync(string? index, CancellationToken cancellationToken = default);

    /// <summary>The owner's "none of the suggestions fits" for the work <paramref name="workId"/>: it leaves the review queue
    /// and the confident list, and "Accept all confident" skips it. <see cref="LibraryLinkStatus.NotFound"/> without such a
    /// work.</summary>
    Task<LibraryLinkStatus> MarkNoneFitsAsync(Guid workId, Guid ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Takes the owner's "none fits" back: the work is in the queue again.</summary>
    Task<LibraryLinkStatus> ClearNoneFitsAsync(Guid workId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the works without a TMDb id whose suggestion is confident (decision 8) — those the owner marked "none
    /// fits" left out —, by title, of the library <paramref name="locationId"/> or of all; with each library's numbers:
    /// its works without an id, and how many of them have a confident suggestion.
    /// </summary>
    Task<ConfidentSuggestionListResult> ListConfidentAsync(string? locationId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// "Accept all confident": the ticked works of a page (at most 100), each linked to the film or show the click names
    /// only while that is still its confident suggestion — computed again now —, else skipped with the reason; the links are
    /// written into the libraries in the background (<see cref="ILibraryLinkService.LinkBatchAsync"/>).
    /// </summary>
    Task<LibraryLinkAcceptResult> AcceptConfidentAsync(LibraryLinkAcceptRequest? request, Guid ownerUserId, CancellationToken cancellationToken = default);
}
