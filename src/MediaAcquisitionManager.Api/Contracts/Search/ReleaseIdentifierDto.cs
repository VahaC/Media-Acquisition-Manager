using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A registry id a release's listing gives (MAM-12); the source that gave it is the
/// release's.</summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id as the app read it (<c>tt0816692</c>, <c>157336</c>).</param>
public sealed record ReleaseIdentifierDto(MediaIdentifierScheme Scheme, string Value);
