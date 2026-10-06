using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace MediaAcquisitionManager.Api.Auth;

/// <summary>
/// MAM-34 — makes every action in the <c>Controllers.Library.Links</c> namespace owner-only, reads included: each
/// action of a controller there, or in a namespace below it, gets <see cref="InstallationOwnerFilter"/>.
/// </summary>
/// <remarks>
/// <para>
/// R19 — the TMDb links' endpoints are the owner's alone (decision 5): setting, replacing, removing and writing links
/// change the owner's library, and reading the suggestions, the review queue, the confident list and the lists of links
/// shows why the owner's library refused a write and calls the owner's catalog. By where a controller lives, the
/// <see cref="SettingsWritesAreOwnerOnlyConvention"/> way, not by an attribute each new action has to remember — and,
/// unlike there, the reads too. Members still see a linked work's ids on its card, which the Library page's own
/// endpoints serve.
/// </para>
/// <para>
/// LibraryLinksAreOwnerOnlyHttpTests sends every method of every endpoint it finds there as a member.
/// </para>
/// </remarks>
public sealed class LibraryLinksAreOwnerOnlyConvention : IControllerModelConvention
{
    /// <summary>The namespace whose controllers are owner-only, the ones below it included.</summary>
    public const string LinksNamespace = "MediaAcquisitionManager.Api.Controllers.Library.Links";

    public void Apply(ControllerModel controller)
    {
        if (!IsLinksController(controller))
            return;

        foreach (var action in controller.Actions.Where(action => !IsGated(controller, action)))
            action.Filters.Add(new RequireInstallationOwnerAttribute());
    }

    private static bool IsLinksController(ControllerModel controller) =>
        controller.ControllerType.Namespace is { } controllerNamespace
        && (controllerNamespace.Equals(LinksNamespace, StringComparison.Ordinal)
            || controllerNamespace.StartsWith(LinksNamespace + ".", StringComparison.Ordinal));

    // Already owner-only, by an attribute on the controller or the action: a second copy of the filter would only
    // repeat the same lookup.
    private static bool IsGated(ControllerModel controller, ActionModel action) =>
        controller.Filters.Concat(action.Filters).OfType<RequireInstallationOwnerAttribute>().Any();
}
