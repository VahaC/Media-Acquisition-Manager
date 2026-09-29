namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One path mapping of an integration, as stored (ADR 0003).</summary>
/// <param name="ServicePrefix">The folder as the service sees it, in canonical form (<c>/downloads</c>).</param>
/// <param name="AppPrefix">The same folder as this app sees it, in canonical form (<c>/media</c>).</param>
public sealed record PathMappingDto(string ServicePrefix, string AppPrefix);
