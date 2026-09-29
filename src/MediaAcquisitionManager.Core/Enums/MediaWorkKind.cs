namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What a media work is. Stored by name, so a member added later needs no migration.
/// </summary>
/// <remarks>
/// Movies only in Phase A. An episode joins with TV support, which waits for specification §7,
/// question 3.
/// </remarks>
public enum MediaWorkKind
{
    /// <summary>A film.</summary>
    Movie = 1,
}
