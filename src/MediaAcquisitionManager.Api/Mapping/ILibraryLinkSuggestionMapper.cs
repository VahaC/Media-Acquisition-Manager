using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Api.Mapping;

/// <summary>MAM-34 — TMDb suggestions of library works, and their preparations, into what the TMDb dialog and the TMDb
/// page show.</summary>
/// <remarks>
/// Every endpoint showing them is the installation owner's (R19), so who asked for a run and why it failed are always in.
/// </remarks>
public interface ILibraryLinkSuggestionMapper
{
    /// <summary>
    /// One suggestion: <paramref name="ranked"/>'s evidence and whether it is confident, with what the stored catalog
    /// work <paramref name="work"/> says of it (overview, poster, IMDb id), and the season and episode to prefill for a
    /// show suggested to a film or an episode.
    /// </summary>
    LinkCandidateDto ToCandidateDto(RankedCandidate ranked, CatalogWorkEntity work, int? seasonNumber, int? episodeNumber);

    /// <summary>One preparation of the suggestions.</summary>
    LibraryLinkSuggestionRunDto ToRunDto(LibraryLinkSuggestionRunEntity run);
}
