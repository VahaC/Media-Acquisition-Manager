namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>MAM-34 — a <c>POST /Items/RemoteSearch/{Movie|Series}</c> a <see cref="FakeJellyfinServer"/> received.</summary>
/// <param name="Endpoint">The search it was sent to: <c>Movie</c> or <c>Series</c>.</param>
/// <param name="Body">The body exactly as it was sent.</param>
public sealed record FakeJellyfinSearch(string Endpoint, string Body);
