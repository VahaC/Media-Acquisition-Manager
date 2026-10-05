namespace MediaAcquisitionManager.Core.Entities;

/// <summary>
/// MAM-33 — what the last check of the release sources found for a catalog work: whether any carries it,
/// and the best of what they carry. Shared by every user and kept across runs, keyed by the work, so a work
/// checked for one user is not searched for again for another (ADR 0024).
/// </summary>
/// <remarks>
/// Kept apart from the searches users run (ADR 0011's one-day searches are not touched):
/// <see cref="AuditableEntity.CreatedUtc"/> is when the work was first checked.
/// </remarks>
public class ReleaseAvailabilityEntity : AuditableEntity
{
    /// <summary>The catalog work; one row per work.</summary>
    public Guid CatalogWorkId { get; set; }

    /// <summary>When the release sources were last asked.</summary>
    public DateTime CheckedUtc { get; set; }

    /// <summary>Whether any release matched the work.</summary>
    public bool Found { get; set; }

    /// <summary>How many releases matched; 0 when none did.</summary>
    public int ReleaseCount { get; set; }

    /// <summary>The best resolution class a matching release claims (2160 for 2160p); <c>null</c> when none
    /// claims one.</summary>
    public int? BestResolution { get; set; }

    /// <summary>Whether a matching release claims a Ukrainian audio track.</summary>
    public bool HasUkrainianAudio { get; set; }

    /// <summary>The most seeders a matching release has; <c>null</c> when no source reports seeders.</summary>
    public int? MaxSeeders { get; set; }
}
