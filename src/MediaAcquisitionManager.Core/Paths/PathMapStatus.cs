namespace MediaAcquisitionManager.Core.Paths;

/// <summary>How a service path mapped onto the app's mounts (ADR 0003) — MAM-30, or an app path back onto the
/// service's (<see cref="PathMapper.MapBack"/>).</summary>
public enum PathMapStatus
{
    /// <summary>The integration has no mappings, so the path is the same on both sides as it is.</summary>
    Identity = 1,

    /// <summary>Exactly one place the mappings send the path to.</summary>
    Mapped = 2,

    /// <summary>The integration has mappings and none covers the path. Identity is not a fallback:
    /// the owner said how this service's paths map, and this one isn't among them.</summary>
    NotCovered = 3,

    /// <summary>The mappings send the path to different places — MAM-30, or lead to it from different places. Never
    /// settled by the longest prefix: that is the guess ADR 0003 forbids.</summary>
    Ambiguous = 4,

    /// <summary>The path isn't a full path the app can map, or can't be written in the style of the prefix it
    /// lands under.</summary>
    Invalid = 5,
}
