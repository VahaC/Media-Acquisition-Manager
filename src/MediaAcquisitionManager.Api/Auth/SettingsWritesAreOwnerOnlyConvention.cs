using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// Makes every write in the <c>Controllers.Settings</c> namespace owner-only: each action of a
/// controller there, or in a namespace below it, that isn't limited to GET or HEAD gets
/// <see cref="InstallationOwnerFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// MAM-4 — by where a controller lives, not by an attribute someone has to remember: a controller
/// dropped into <c>Controllers/Settings</c> has its writes gated from its first build, and a missing
/// <see cref="RequireInstallationOwnerAttribute"/> can't open a global setting to every signed-in
/// user. SettingsWritesAreOwnerOnlyHttpTests sends every settings write it finds as a non-owner;
/// SettingsWritesAreOwnerOnlyConventionHttpTests checks this convention alone, on a test settings
/// controller that carries no attribute.
/// </para>
/// <para>
/// Fail closed: an action with no HTTP-method constraint answers every method, POST and DELETE
/// included, so it counts as a write. Reads are left to the controller: some settings may be read by
/// everyone, and the ones that may not carry <see cref="RequireInstallationOwnerAttribute"/>.
/// </para>
/// </remarks>
public sealed class SettingsWritesAreOwnerOnlyConvention : IControllerModelConvention
{
    /// <summary>The namespace whose controllers' writes are owner-only, the ones below it included.</summary>
    public const string SettingsNamespace = "MediaAcquisitionManager.Api.Controllers.Settings";

    public void Apply(ControllerModel controller)
    {
        if (!IsSettingsController(controller))
            return;

        foreach (var action in controller.Actions.Where(action => !IsReadOnly(action) && !IsGated(controller, action)))
            action.Filters.Add(new RequireInstallationOwnerAttribute());
    }

    private static bool IsSettingsController(ControllerModel controller) =>
        controller.ControllerType.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(SettingsNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(SettingsNamespace + ".", StringComparison.Ordinal));

    // Read-only only when every route of the action is limited to GET or HEAD; one route with no
    // method constraint (or none at all) makes it a write.
    private static bool IsReadOnly(ActionModel action) =>
        action.Selectors.Count > 0
        && action.Selectors.All(selector =>
            selector.EndpointMetadata.OfType<IHttpMethodMetadata>().SelectMany(metadata => metadata.HttpMethods).ToList() is { Count: > 0 } methods
            && methods.All(method => HttpMethods.IsGet(method) || HttpMethods.IsHead(method)));

    // Already owner-only as a whole (UsersController, whose reads are owner-only too): a second copy
    // of the filter would only repeat the same lookup.
    private static bool IsGated(ControllerModel controller, ActionModel action) =>
        controller.Filters.Concat(action.Filters).OfType<RequireInstallationOwnerAttribute>().Any();
}
