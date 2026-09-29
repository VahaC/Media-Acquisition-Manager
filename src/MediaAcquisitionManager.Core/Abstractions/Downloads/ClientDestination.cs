namespace MediaAcquisitionManager.Core.Abstractions.Downloads;

/// <summary>One of a download client's own destinations, such as a category.</summary>
/// <param name="Key">The client's name for it: what a submission sends.</param>
/// <param name="DisplayName">What to show for it.</param>
/// <param name="ServicePath">The folder it saves to as the client sees it; <c>null</c> when the client
/// doesn't say.</param>
public sealed record ClientDestination(string Key, string DisplayName, string? ServicePath);
