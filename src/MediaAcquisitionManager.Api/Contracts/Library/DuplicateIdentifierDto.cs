using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-20 — a registry id that makes works one (ADR 0017): a work's own, or its series' with a season and
/// an episode.</summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id.</param>
/// <param name="SeasonNumber">A series' id's season; <c>null</c> for a work's own id.</param>
/// <param name="EpisodeNumber">A series' id's episode; <c>null</c> for a work's own id.</param>
public sealed record DuplicateIdentifierDto(MediaIdentifierScheme Scheme, string Value, int? SeasonNumber, int? EpisodeNumber);
