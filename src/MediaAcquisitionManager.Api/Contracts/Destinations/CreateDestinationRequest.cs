namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A new destination, added by the installation owner (MAM-11).</summary>
/// <remarks>
/// MAM-11 — every field nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c>
/// would bring MVC's implicit <c>[Required]</c> back): a blank or missing field must reach the destination
/// rules, whose sentence the Add destination dialog shows, not stop at a validation body with no
/// <c>error</c> in it. No save path, and no field that could carry one: the client's category decides
/// where a download goes.
/// </remarks>
/// <param name="Name">Its name, unique per download client in any letter case.</param>
/// <param name="DownloadClientId">The download client integration it belongs to.</param>
/// <param name="ClientDestinationKey">The client's own name for the category, exactly as the client lists
/// it; the client is asked whether it has it before anything is saved.</param>
/// <param name="IsDefault">Whether it becomes the installation's default destination, taking that over from
/// the one that is; left out or <c>false</c> makes it the default only when it is the first destination.</param>
/// <param name="Sections">MAM-32 — the release sources' sections whose releases the Download dialog sends
/// here first; one mapped to another destination moves here. Left out or empty maps none.</param>
public sealed record CreateDestinationRequest(
    string? Name,
    Guid? DownloadClientId,
    string? ClientDestinationKey,
    bool? IsDefault = null,
    IReadOnlyList<DestinationSectionRequest?>? Sections = null);
