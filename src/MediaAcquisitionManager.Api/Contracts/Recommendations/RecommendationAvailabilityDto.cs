namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-33 — what the last check of the release sources found for a work, shared by every user
/// (ADR 0024).</summary>
/// <param name="CheckedUtc">When the sources were last asked.</param>
/// <param name="Found">Whether any release matched the work.</param>
/// <param name="ReleaseCount">How many releases matched; 0 when none did.</param>
/// <param name="BestResolution">The best resolution class a matching release claims (2160 for 2160p); <c>null</c> when
/// none claims one.</param>
/// <param name="HasUkrainianAudio">Whether a matching release claims a Ukrainian audio track.</param>
/// <param name="MaxSeeders">The most seeders a matching release has; <c>null</c> when no source reports
/// seeders.</param>
public sealed record RecommendationAvailabilityDto(
    DateTime CheckedUtc,
    bool Found,
    int ReleaseCount,
    int? BestResolution,
    bool HasUkrainianAudio,
    int? MaxSeeders);
