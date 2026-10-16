namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>MAM-5 — the signed-in user's personal access tokens.</summary>
/// <remarks>Unpaged on purpose: one person's own tokens, a handful, not a list that grows with the household's
/// library (ADR 0040).</remarks>
/// <param name="Items">Every token of theirs not revoked, the expired ones included, newest first.</param>
public sealed record PersonalAccessTokenListResponse(IReadOnlyList<PersonalAccessTokenDto> Items);
