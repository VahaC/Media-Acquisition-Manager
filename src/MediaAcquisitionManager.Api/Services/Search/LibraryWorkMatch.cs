using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A library work a search matches (MAM-12), with what the search page shows of it and what its
/// releases are matched against.</summary>
/// <param name="Work">The work as the Library page shows it.</param>
/// <param name="Match">Why it matched, and which ids disagree.</param>
/// <param name="Titles">Every title any source gave the work, in title order — what a release's claimed
/// titles are matched against.</param>
/// <param name="Identifiers">Every id any source gave the work — what a release listing's ids are matched
/// against.</param>
/// <param name="MatchedIdentifiers">The work's identifier rows whose ids matched, every source's claim of
/// them included; empty unless the match is by an identifier.</param>
/// <param name="MatchedTitle">The title row that matched, with the source that reported it; <c>null</c>
/// when only an id matched.</param>
public sealed record LibraryWorkMatch(
    LibraryWorkDto Work,
    WorkMatch Match,
    IReadOnlyList<ReportedTitle> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<MediaWorkIdentifierEntity> MatchedIdentifiers,
    MediaWorkTitleEntity? MatchedTitle);
