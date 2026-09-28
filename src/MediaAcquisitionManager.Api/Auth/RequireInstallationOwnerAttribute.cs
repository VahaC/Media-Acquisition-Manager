using Microsoft.AspNetCore.Mvc;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// Makes a controller or an action owner-only through <see cref="InstallationOwnerFilter"/>. Needed
/// for owner-only reads, and for owner-only writes outside <c>Controllers/Settings</c>; the writes
/// inside it are gated by <see cref="SettingsWritesAreOwnerOnlyConvention"/> without it.
/// </summary>
/// <remarks>
/// A type filter: every request gets a filter of its own, built from the request's services, so it
/// checks with the request's DbContext.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireInstallationOwnerAttribute() : TypeFilterAttribute(typeof(InstallationOwnerFilter));
