using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-24 — makes every action in the <c>Controllers.Replacements</c> namespace owner-only, reads included: each action of
/// a controller there, or in a namespace below it, gets <see cref="InstallationOwnerFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// ADR 0033 — only the installation owner schedules a replacement, and later retries, resolves, restores and deletes
/// for good, "enforced as the other owner-only writes are, by a test that discovers the routes". The reads too: the
/// Download dialog's read shows which library folders the owner ticked, his files' paths, and his download clients'
/// torrents. By where a controller lives, the <see cref="LibraryLinksAreOwnerOnlyConvention"/> way, not by an attribute
/// each new action has to remember; a member's Download dialog never asks, and keeps Keep both.
/// </para>
/// <para>
/// ReplacementsAreOwnerOnlyHttpTests sends every method of every endpoint it finds there as a member.
/// </para>
/// </remarks>
public sealed class ReplacementsAreOwnerOnlyConvention : IControllerModelConvention
{
    /// <summary>The namespace whose controllers are owner-only, the ones below it included.</summary>
    public const string ReplacementsNamespace = "MediaAcquisitionManager.Api.Controllers.Replacements";

    public void Apply(ControllerModel controller)
    {
        if (!IsReplacementsController(controller))
            return;

        foreach (var action in controller.Actions.Where(action => !IsGated(controller, action)))
            action.Filters.Add(new RequireInstallationOwnerAttribute());
    }

    private static bool IsReplacementsController(ControllerModel controller) =>
        controller.ControllerType.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(ReplacementsNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(ReplacementsNamespace + ".", StringComparison.Ordinal));

    // Already owner-only, by an attribute on the controller or the action: a second copy of the filter would only repeat
    // the same lookup.
    private static bool IsGated(ControllerModel controller, ActionModel action) =>
        controller.Filters.Concat(action.Filters).OfType<RequireInstallationOwnerAttribute>().Any();
}
