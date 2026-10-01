namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The folders inside the app where it may read files (MAM-9), behind the Media folders
/// settings page.</summary>
/// <param name="Paths">Every allowed root in canonical form, in the order the owner entered them; none
/// until the owner names one.</param>
public sealed record AllowedRootsResponse(IReadOnlyList<string> Paths);
