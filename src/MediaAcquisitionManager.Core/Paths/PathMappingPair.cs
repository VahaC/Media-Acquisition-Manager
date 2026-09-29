namespace MediaAcquisitionManager.Core.Paths;

/// <summary>One prefix pair of an integration's path mapping (ADR 0003).</summary>
/// <param name="ServicePrefix">The folder as the service sees it; canonical once stored.</param>
/// <param name="AppPrefix">The same folder as this app sees it; canonical once stored.</param>
public sealed record PathMappingPair(string ServicePrefix, string AppPrefix);
