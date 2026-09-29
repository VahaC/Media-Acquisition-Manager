using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Media;

/// <summary>A title as a service reports it, with the year it gives alongside.</summary>
/// <param name="Text">The title as the service wrote it.</param>
/// <param name="Kind">Displayed or original.</param>
/// <param name="Year">The year the service gives with it; <c>null</c> when none.</param>
public sealed record ReportedTitle(string Text, MediaTitleKind Kind, int? Year);
