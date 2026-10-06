using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Audit;

/// <summary>
/// MAM-34 — an id an audit row records (<c>AuditEventIdentifierEntity</c>): what a link held before it was set, replaced
/// or removed, or what the library held for an item before and after an identification. They are rows of their own, so
/// a reader never has to parse a text back; nothing is restored from them (ADR 0026, 0.2.1).
/// </summary>
/// <param name="Role">Whether it is what the change found or what it left.</param>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id.</param>
public sealed record AuditIdentifier(AuditIdentifierRole Role, MediaIdentifierScheme Scheme, string Value);
