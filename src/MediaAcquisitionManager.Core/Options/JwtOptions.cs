namespace MediaAcquisitionManager.Core.Options;

/// <summary>
/// The key access tokens are signed with. Bound from the <c>Jwt</c> section
/// (<c>MAM_Jwt__Secret</c> in the environment); when nothing configures it, the app generates one on
/// its first start and keeps it in the data directory's <c>.secrets</c>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Base64 of the HMAC-SHA256 key (the generated one is 64 random bytes, the least accepted is
    /// 32). Changing it signs nobody out: access tokens signed with the old key stop validating, and
    /// the SPA quietly gets new ones through the refresh cookie, which this key doesn't sign.
    /// </summary>
    public string? Secret { get; set; }
}
