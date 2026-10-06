using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-34 — the linked neighbour a TMDb suggestion looks related to.</summary>
/// <param name="Reason">What they share.</param>
/// <param name="SiblingTitle">The neighbour's title.</param>
public sealed record SiblingEvidenceDto(LinkSiblingReason Reason, string SiblingTitle);
