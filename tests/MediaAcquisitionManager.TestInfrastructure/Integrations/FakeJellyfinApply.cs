using System.Text.Json.Nodes;

namespace MediaAcquisitionManager.TestInfrastructure.Integrations;

/// <summary>
/// MAM-34 — a <c>POST /Items/RemoteSearch/Apply/{id}</c> a <see cref="FakeJellyfinServer"/> took: found its item and was not
/// refused. Recorded whatever the server then did to the item — <see cref="FakeJellyfinServer.IgnoresApply"/> and
/// <see cref="FakeJellyfinServer.AppliesIdsOnly"/> still count, and so does an Apply whose answer a test then loses.
/// </summary>
/// <param name="ItemId">The item's id as it was posted (any form Jellyfin takes).</param>
/// <param name="ReplaceAllImages">The raw value of the <c>replaceAllImages</c> query parameter; <c>null</c> when absent.</param>
/// <param name="Row">The body: the search result the item was applied.</param>
/// <param name="StartedAt">When the server began on it, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</param>
/// <param name="EndedAt">When it had applied it, in the same ticks: two Applies whose spans overlap ran at once.</param>
public sealed record FakeJellyfinApply(string ItemId, string? ReplaceAllImages, JsonObject Row, long StartedAt, long EndedAt);
