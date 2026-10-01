using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Entities;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>A library work a search lists (MAM-12) — one it matches, or one holding every word of its
/// title — with what the search page shows of it and what its releases are matched against.</summary>
/// <param name="Work">The work as the Library page shows it.</param>
/// <param name="Match">Why it matched, or <c>MatchEvidence.Words</c> when it only holds the words; and
/// which ids disagree.</param>
/// <param name="Titles">Every title any source gave the work, in title order — what a release's claimed
/// titles are matched against.</param>
/// <param name="Identifiers">Every id any source gave the work — what a release listing's ids are matched
/// against.</param>
/// <param name="MatchedIdentifiers">The work's identifier rows whose ids matched, every source's claim of
/// them included; empty unless the match is by an identifier.</param>
/// <param name="MatchedTitle">The title row that matched, or that holds the words, with the source that
/// reported it; <c>null</c> when only an id matched, or only a file path holds the words.</param>
public sealed record LibraryWorkMatch(
    LibraryWorkDto Work,
    WorkMatch Match,
    IReadOnlyList<ReportedTitle> Titles,
    IReadOnlyList<MediaIdentifier> Identifiers,
    IReadOnlyList<MediaWorkIdentifierEntity> MatchedIdentifiers,
    MediaWorkTitleEntity? MatchedTitle);
