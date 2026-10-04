using MediaAcquisitionManager.Core.Abstractions.Search;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>What one release source lists as its categories right now, or why it couldn't be asked
/// (MAM-32).</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints the list as its type name, and the error is masked
/// of every secret of the call before it gets here.
/// </remarks>
/// <param name="Categories">Its categories in its own order; empty when it couldn't be asked.</param>
/// <param name="Error">The sentence saying why it couldn't be asked; <c>null</c> when it answered.</param>
public sealed record ReleaseSourceCategories(IReadOnlyList<ReleaseCategory> Categories, string? Error)
{
    public static ReleaseSourceCategories Listed(IReadOnlyList<ReleaseCategory> categories) => new(categories, null);

    public static ReleaseSourceCategories NotListed(string error) => new([], error);
}
