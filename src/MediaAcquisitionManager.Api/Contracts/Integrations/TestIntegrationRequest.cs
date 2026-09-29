namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>An unsaved integration to test from the Add or Edit dialog; nothing of it is stored.</summary>
/// <remarks>
/// <para>
/// MAM-7 — every field nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c>
/// would bring MVC's implicit <c>[Required]</c> back): a blank or missing field must reach the
/// integration rules, whose sentence the dialog shows, not stop at a validation body with no
/// <c>error</c> in it.
/// </para>
/// <para>
/// Without <paramref name="IntegrationId"/> it is a new integration of <paramref name="AdapterType"/>,
/// and Keep means no secret. With it, it is the edit of that integration: its stored type is used
/// whatever <paramref name="AdapterType"/> says, and a secret kept or left out is the stored one — the
/// owner can test an edit without typing its credentials again.
/// </para>
/// </remarks>
/// <param name="IntegrationId">The integration being edited, or <c>null</c> for a new one.</param>
/// <param name="AdapterType">The type of service of a new integration.</param>
/// <param name="BaseUrl">The address to test.</param>
/// <param name="UserName">The user name to test with; blank means none.</param>
/// <param name="Password">Set tests with the value, Clear with none, Keep with the stored one.</param>
/// <param name="ApiKey">Set tests with the value, Clear with none, Keep with the stored one.</param>
public sealed record TestIntegrationRequest(
    Guid? IntegrationId,
    string? AdapterType,
    string? BaseUrl,
    string? UserName = null,
    SecretValueUpsert? Password = null,
    SecretValueUpsert? ApiKey = null);
