namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>The categories a download client offers right now, each with its check (MAM-11).</summary>
/// <remarks>
/// MAM-11 — a live call, so it answers 200 whatever the client said (ADR 0005): "it couldn't be asked,
/// because …" is the answer to "what does it offer?", not a failure of this API.
/// </remarks>
/// <param name="Items">The client's categories in its own order; empty when it couldn't be asked.</param>
/// <param name="Error">The sentence saying why the client couldn't be asked — switched off, a type this
/// version of the app can't use, a stored secret it can't read, or the client's own failure; <c>null</c>
/// when it answered.</param>
public sealed record ClientDestinationListResponse(IReadOnlyList<ClientDestinationDto> Items, string? Error);
