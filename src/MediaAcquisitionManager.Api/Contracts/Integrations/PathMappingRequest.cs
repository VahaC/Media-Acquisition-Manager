namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>One path mapping of an integration as the owner typed it (ADR 0003).</summary>
/// <remarks>
/// MAM-7 — both nullable and without <c>[Required]</c> on purpose (a non-nullable <c>string</c> would
/// bring MVC's implicit <c>[Required]</c> back): a blank or missing folder must reach the path-mapping
/// rules, whose sentence the dialog shows, not stop at a validation body with no <c>error</c> in it.
/// </remarks>
/// <param name="ServicePrefix">The folder as the service sees it, such as <c>/downloads</c>.</param>
/// <param name="AppPrefix">The same folder as this app sees it, such as <c>/media</c>.</param>
public sealed record PathMappingRequest(string? ServicePrefix, string? AppPrefix);
