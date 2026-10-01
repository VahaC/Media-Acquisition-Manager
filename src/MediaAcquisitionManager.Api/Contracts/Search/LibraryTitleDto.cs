using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Contracts;

/// <summary>A title a source reported for a library work, with who reported it (MAM-12, ADR 0006).</summary>
/// <param name="Text">The title as the source wrote it.</param>
/// <param name="Kind">Displayed or original.</param>
/// <param name="Year">The year the source gave with it; <c>null</c> when none.</param>
/// <param name="SourceName">The name of the integration that reported it; <c>null</c> when no integration
/// did.</param>
public sealed record LibraryTitleDto(string Text, MediaTitleKind Kind, int? Year, string? SourceName);
