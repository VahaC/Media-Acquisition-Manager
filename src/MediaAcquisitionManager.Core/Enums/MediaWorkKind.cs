namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What a media work is. Stored by name, so a member added later needs no migration.
/// </summary>
/// <remarks>
/// Movies only in Phase A. An episode joins with TV support, which MAM-17 decided for Phase B
/// (MAM-32, ADR 0017).
/// </remarks>
public enum MediaWorkKind
{
    /// <summary>A film.</summary>
    Movie = 1,
}
