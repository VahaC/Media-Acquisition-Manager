namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// What a release is, by the source's own word: the category it is listed in (MAM-32, ADR 0020).
/// Stored by name.
/// </summary>
/// <remarks>
/// A TV release is matched to library series only, and a movie release to movies only, just as the
/// library alone says what an item is (ADR 0017): a title shared by a film and a show never makes one the
/// other.
/// </remarks>
public enum ReleaseKind
{
    /// <summary>Listed in a movie category, or in no category the source calls TV.</summary>
    Movie = 1,

    /// <summary>Listed in a TV category: a series, a season or episodes, cartoon series included.</summary>
    Tv = 2,
}
