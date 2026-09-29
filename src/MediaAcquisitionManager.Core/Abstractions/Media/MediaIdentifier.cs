using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Media;

/// <summary>
/// A registry id as a service reports it. Adapters report values only; the core stamps provenance,
/// because it knows which connection it called (ADR 0006).
/// </summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id as given (<c>tt0090605</c>, <c>679</c>).</param>
public sealed record MediaIdentifier(MediaIdentifierScheme Scheme, string Value);
