using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Media;

/// <summary>MAM-34 — an id the owner pasted to link a library work, as <see cref="IdentifierReferenceParser"/> read it.</summary>
/// <param name="Scheme">The registry: a TMDb film (<see cref="MediaIdentifierScheme.Tmdb"/>), a TMDb show
/// (<see cref="MediaIdentifierScheme.TmdbTv"/>) or IMDb; <c>null</c> for a bare number, which could be either of TMDb's
/// two (ADR 0017) — the owner must say which.</param>
/// <param name="Value">The id, IMDb's with its <c>tt</c> lower-cased.</param>
/// <param name="SeasonNumber">The season a TMDb episode or season link names; <c>null</c> otherwise.</param>
/// <param name="EpisodeNumber">The episode a TMDb episode link names; <c>null</c> otherwise.</param>
public sealed record IdentifierReference(MediaIdentifierScheme? Scheme, string Value, int? SeasonNumber = null, int? EpisodeNumber = null);
