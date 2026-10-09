using MediaAcquisitionManager.Core.Paths;

namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>MAM-24 — what every download client's whole listing says of the old copies (ADR 0031).</summary>
/// <param name="Holds">The torrents holding each copy, by its app path; a copy no torrent was found to hold isn't
/// here.</param>
/// <param name="Clients">How each download client was read, in name order.</param>
public sealed record ReplacementHoldings(
    IReadOnlyDictionary<NormalizedPath, IReadOnlyList<ReplacementHold>> Holds,
    IReadOnlyList<ReplacementClientReading> Clients)
{
    /// <summary>No client read: the dialog didn't ask, or nothing could be ticked.</summary>
    public static ReplacementHoldings None { get; } = new(new Dictionary<NormalizedPath, IReadOnlyList<ReplacementHold>>(), []);
}
