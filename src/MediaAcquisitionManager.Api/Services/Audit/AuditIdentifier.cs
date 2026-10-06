using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>
/// MAM-34 — an id an audit row records (<c>AuditEventIdentifierEntity</c>): what the library held for an item before
/// a link was set or written, or after. A restore starts from them, so they are rows of their own, not part of a text.
/// </summary>
/// <param name="Role">Whether it is what the change found or what it left.</param>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id.</param>
public sealed record AuditIdentifier(AuditIdentifierRole Role, MediaIdentifierScheme Scheme, string Value);
