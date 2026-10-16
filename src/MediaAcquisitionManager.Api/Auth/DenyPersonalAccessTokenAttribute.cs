namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-5 — no personal access token may call this endpoint, whatever its access, a GET included: the account, its
/// password and sessions, token management, and everything under <c>Controllers.Settings</c> (added there by
/// <see cref="SettingsWritesAreOwnerOnlyConvention"/>). A token gets 403 with
/// <see cref="PersonalAccessTokenErrors.NotForTokens"/> (ADR 0040).
/// </summary>
/// <remarks>
/// Endpoint metadata, read by <see cref="PersonalAccessTokenAccessHandler"/> in the default policy, so it works on any
/// endpoint — a minimal one too, as MAM-45's <c>/mcp</c> will be. A filter would only work on controllers.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DenyPersonalAccessTokenAttribute : Attribute;
