namespace MediaAcquisitionManager.Core.Abstractions.Search;

/// <summary>
/// Where the server fetches a release's payload from: opaque, and server-only (ADR 0004).
/// </summary>
/// <param name="Value">The link or magnet exactly as the source gave it.</param>
public sealed record ReleasePayloadLocator(string Value)
{
    /// <summary>A masked stand-in.</summary>
    /// <remarks>
    /// MAM-7 — a gateway's download link carries its API key in the query (ADR 0004), and a record's
    /// generated <c>ToString</c> would put it in the first log line that mentions a hit.
    /// </remarks>
    public override string ToString() => $"ReleasePayloadLocator {{ {SecretRedaction.Mask} }}";
}
