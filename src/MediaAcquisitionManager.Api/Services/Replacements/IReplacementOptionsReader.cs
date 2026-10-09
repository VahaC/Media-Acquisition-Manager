namespace MediaAcquisitionManager.Api.Services.Replacements;

/// <summary>
/// MAM-24 — the Download dialog's question what of a release the installation owner may replace through a destination
/// (ADR 0031–0033): the reason nothing can, or the matched works with their copies, each copy's reason and marks.
/// </summary>
/// <remarks>
/// <para>
/// The release-level reasons, the first that applies: replacement switched off — no client asked, no work read —; the
/// release a magnet link; the destination unusable for a replacement — its client couldn't say where it saves, its
/// folder isn't confirmed inside a library, or the app can't look into it for files already there (R1) —; no matched
/// work. The destination's client is asked for its categories, as the overwrite check asks it; that is the only client
/// call unless the torrents are asked for.
/// </para>
/// <para>
/// R2 — every download client's torrents are read only on the dialog's word (<c>holdings</c>), and only while something
/// can be ticked: the gates open, a usable destination and at least one copy no reason stands in front of.
/// </para>
/// <para>
/// It checks nothing about who calls: the route is the owner's alone by where its controller lives
/// (<c>ReplacementsAreOwnerOnlyConvention</c>). Writes nothing.
/// </para>
/// </remarks>
public interface IReplacementOptionsReader
{
    /// <summary>What of <paramref name="releaseId"/>, one of the caller's searches', the owner may replace through
    /// <paramref name="destinationId"/>; with the download clients' torrents read when <paramref name="holdings"/> says
    /// "true".</summary>
    Task<ReplacementOptionsResult> ReadAsync(
        string? releaseId, string? destinationId, string? holdings, Guid callerUserId, CancellationToken cancellationToken = default);
}
