using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>The outcome of <see cref="ISearchService.GetAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Search">The search when <see cref="SearchReadStatus.Found"/>.</param>
public sealed record SearchReadResult(SearchReadStatus Status, SearchResponse? Search)
{
    public static SearchReadResult NotFound { get; } = new(SearchReadStatus.NotFound, null);

    public static SearchReadResult Found(SearchResponse search) => new(SearchReadStatus.Found, search);
}
