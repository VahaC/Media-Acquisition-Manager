using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Matching;

/// <summary>
/// Why a work matched a <see cref="WorkClaim"/>, and what disagrees (MAM-12). Evidence to show, never a
/// decision: nothing is picked, merged or acted on by it.
/// </summary>
/// <param name="Evidence">The strongest evidence found: an identifier, then the title and the year, then
/// the title alone (a candidate to check by hand).</param>
/// <param name="MatchedIdentifiers">The work's ids that equal one sought, each once as written, in the
/// work's order; empty unless <paramref name="Evidence"/> is <see cref="MatchEvidence.Identifier"/>.</param>
/// <param name="MatchedTitle">The work's title that matched a title sought; <c>null</c> when only an id
/// matched.</param>
/// <param name="ConflictingSchemes">Every scheme, in scheme order, whose values disagree: the claim and the
/// work each hold one and share none, or the work itself holds two (two sources disagree, ADR 0006).
/// Shown, never resolved.</param>
public sealed record WorkMatch(
    MatchEvidence Evidence,
    IReadOnlyList<MediaIdentifier> MatchedIdentifiers,
    ReportedTitle? MatchedTitle,
    IReadOnlyList<MediaIdentifierScheme> ConflictingSchemes);
