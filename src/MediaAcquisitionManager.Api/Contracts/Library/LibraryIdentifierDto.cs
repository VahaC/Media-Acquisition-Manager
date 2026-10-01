using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A registry id a source claims for a work (ADR 0006).</summary>
/// <param name="Scheme">The registry.</param>
/// <param name="Value">The id as the source gave it (<c>tt0090605</c>, <c>679</c>).</param>
/// <param name="SourceName">The name of the integration that claimed it when it did; <c>null</c> when no
/// integration did.</param>
public sealed record LibraryIdentifierDto(MediaIdentifierScheme Scheme, string Value, string? SourceName);
