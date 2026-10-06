using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — how close a TMDb suggestion's titles come to a library work's.</summary>
/// <param name="Similarity">0 to 1.</param>
/// <param name="Exact">Whether one of its titles is one of the work's, as titles are matched.</param>
/// <param name="MatchedKind">Which of its titles came closest.</param>
/// <param name="MatchedTitle">That title.</param>
public sealed record TitleEvidenceDto(double Similarity, bool Exact, LinkTitleKind MatchedKind, string MatchedTitle);
