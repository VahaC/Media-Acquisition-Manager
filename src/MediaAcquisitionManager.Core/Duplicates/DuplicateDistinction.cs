using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>MAM-20 — one thing that tells a duplicate group's files or works apart, flagging it for review.</summary>
/// <param name="Kind">What differs.</param>
/// <param name="Scheme">The registry whose ids disagree, for <see cref="DuplicateDistinctionKind.IdentifierConflict"/>;
/// <c>null</c> otherwise.</param>
/// <param name="OfSeries">Whether the ids that disagree are the series' (two shows behind one episode's ids), not the
/// works' own.</param>
public sealed record DuplicateDistinction(DuplicateDistinctionKind Kind, MediaIdentifierScheme? Scheme, bool OfSeries);
