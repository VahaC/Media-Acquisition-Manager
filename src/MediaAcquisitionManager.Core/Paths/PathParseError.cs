namespace MediaAcquisitionManager.Core.Paths;

/// <summary>Why a text isn't a path <see cref="NormalizedPath.TryParse"/> accepts.</summary>
public enum PathParseError
{
    /// <summary>It parsed.</summary>
    None = 0,

    /// <summary>Nothing, or only whitespace.</summary>
    Blank = 1,

    /// <summary>Not a full path: relative (<c>movies</c>), home-relative (<c>~/x</c>), drive-relative
    /// (<c>C:x</c>) or an incomplete UNC path.</summary>
    NotAbsolute = 2,

    /// <summary>A <c>.</c> or <c>..</c> folder name.</summary>
    DotSegment = 3,

    /// <summary>A line break or another control character.</summary>
    ControlCharacter = 4,
}
