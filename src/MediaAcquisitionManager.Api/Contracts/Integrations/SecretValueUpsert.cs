using MediaAcquisitionManager.Core.Abstractions;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A change to one secret of an integration: <c>{ "mode": "Set", "value": "…" }</c>,
/// <c>{ "mode": "Clear" }</c>, or left out (or <c>null</c>) to keep it.</summary>
/// <remarks>
/// MAM-7 — omitted or <c>null</c> means Keep, so a PUT that leaves a secret out keeps it, and the edit
/// dialog never has to hold a stored value it could never read.
/// </remarks>
/// <param name="Mode">Keep, Set or Clear.</param>
/// <param name="Value">The new secret; read only with <see cref="SecretUpsertMode.Set"/>.</param>
public sealed record SecretValueUpsert(SecretUpsertMode Mode, string? Value = null)
{
    /// <summary>The change with its value masked.</summary>
    /// <remarks>
    /// MAM-7 — MVC logs a controller action's arguments at Trace through their <c>ToString</c>, and a
    /// record's generated one prints every property: raising that log level to debug a save would
    /// write the new password or API key, from inside each request that carries one, in clear (ADR 0007).
    /// </remarks>
    public override string ToString() =>
        $"SecretValueUpsert {{ Mode = {Mode}, Value = {(Value is null ? "null" : SecretRedaction.Mask)} }}";
}
