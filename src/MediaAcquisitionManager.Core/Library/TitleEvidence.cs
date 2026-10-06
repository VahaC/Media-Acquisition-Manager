using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — how close a TMDb candidate's titles come to a library work's.</summary>
/// <param name="Similarity">The best <see cref="TitleSimilarity"/> of any of its titles to any of the work's, 0 to 1.</param>
/// <param name="Exact">Whether one of its titles has the key of one of the work's (<c>TitleMatchKey</c>).</param>
/// <param name="MatchedKind">Which of its titles that was.</param>
/// <param name="MatchedTitle">That title.</param>
public sealed record TitleEvidence(double Similarity, bool Exact, LinkTitleKind MatchedKind, string MatchedTitle);
