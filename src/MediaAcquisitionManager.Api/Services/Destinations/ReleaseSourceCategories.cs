using MediaAcquisitionManager.Core.Abstractions.Search;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Api.Services.Destinations;

/// <summary>What one release source lists as its categories right now, or why it couldn't be asked
/// (MAM-32).</summary>
/// <remarks>
/// Safe to print: the generated <c>ToString</c> prints the list as its type name, and the error is masked
/// of every secret of the call before it gets here.
/// </remarks>
/// <param name="Categories">Its categories in its own order; empty when it couldn't be asked.</param>
/// <param name="Error">Why it couldn't be asked, as a message the Api renders (ADR 0042); <c>null</c> when it
/// answered.</param>
public sealed record ReleaseSourceCategories(IReadOnlyList<ReleaseCategory> Categories, Message? Error)
{
    public static ReleaseSourceCategories Listed(IReadOnlyList<ReleaseCategory> categories) => new(categories, null);

    public static ReleaseSourceCategories NotListed(Message error) => new([], error);
}
