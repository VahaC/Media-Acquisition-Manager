using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Library;

/// <summary>MAM-34 — the linked neighbour a TMDb candidate looks related to.</summary>
/// <param name="Reason">What they share.</param>
/// <param name="SiblingTitle">The neighbour's title.</param>
public sealed record SiblingEvidence(LinkSiblingReason Reason, string SiblingTitle);
