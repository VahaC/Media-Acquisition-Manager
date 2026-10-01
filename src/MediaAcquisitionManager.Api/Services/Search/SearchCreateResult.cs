using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>The outcome of <see cref="ISearchService.CreateAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Search">The stored search when <see cref="SearchCreateStatus.Created"/>.</param>
/// <param name="Error">The sentence saying what the search doesn't take otherwise.</param>
public sealed record SearchCreateResult(SearchCreateStatus Status, SearchResponse? Search, string? Error)
{
    public static SearchCreateResult Created(SearchResponse search) => new(SearchCreateStatus.Created, search, null);

    public static SearchCreateResult Invalid(string error) => new(SearchCreateStatus.Invalid, null, error);
}
