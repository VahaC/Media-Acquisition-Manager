namespace MediaAcquisitionManager.Core.Enums;

/// <summary>
/// The dynamic range of a video track, as the media inspector reads it from the stream (MAM-9).
/// Stored by name; a track whose stream doesn't say has none, which the page shows as unknown — a
/// 10-bit stream without a transfer characteristic is not assumed to be either.
/// </summary>
public enum DynamicRange
{
    /// <summary>A standard transfer characteristic (BT.709, BT.601, sRGB and the like).</summary>
    Sdr = 1,

    /// <summary>The PQ transfer characteristic (SMPTE ST 2084): HDR10, or HDR10+, whose dynamic
    /// metadata the stream summary doesn't show.</summary>
    Hdr10 = 2,

    /// <summary>The HLG transfer characteristic (ARIB STD-B67).</summary>
    Hlg = 3,

    /// <summary>A Dolby Vision configuration record, whatever the base layer's transfer.</summary>
    DolbyVision = 4,
}
