namespace MediaAcquisitionManager.Core.Paths;

/// <summary>
/// MAM-30 — where an app path is as a service sees it, if anywhere: <see cref="PathMapper.MapBack"/>'s answer, the way
/// back of <see cref="PathMapResult"/>. A record of its own so a service path never sits in a field called
/// <c>AppPath</c>. Only <see cref="PathMapStatus.Identity"/> and <see cref="PathMapStatus.Mapped"/> give one, and neither
/// proves the service can see it: the service is asked (ADR 0039).
/// </summary>
/// <param name="Status">How the path mapped back.</param>
/// <param name="ServicePath">The service's path in canonical form; <c>null</c> unless <see cref="HasServicePath"/>.</param>
/// <param name="Reason">A readable sentence saying why there is no service path; <c>null</c> when there is one.</param>
public sealed record ServicePathMapResult(PathMapStatus Status, string? ServicePath, string? Reason)
{
    /// <summary>Whether <see cref="ServicePath"/> is a path the service may be asked about.</summary>
    public bool HasServicePath => Status is PathMapStatus.Identity or PathMapStatus.Mapped;
}
