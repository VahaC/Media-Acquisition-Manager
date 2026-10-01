using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Search;

/// <summary>The outcome of <see cref="ISearchService.ListReleasesAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="List">The page when <see cref="SearchReleaseListStatus.Listed"/>.</param>
/// <param name="Error">The sentence saying what the list doesn't take, when
/// <see cref="SearchReleaseListStatus.Invalid"/>.</param>
public sealed record SearchReleaseListResult(SearchReleaseListStatus Status, SearchReleaseListResponse? List, string? Error)
{
    public static SearchReleaseListResult NotFound { get; } = new(SearchReleaseListStatus.NotFound, null, null);

    public static SearchReleaseListResult Listed(SearchReleaseListResponse list) => new(SearchReleaseListStatus.Listed, list, null);

    public static SearchReleaseListResult Invalid(string error) => new(SearchReleaseListStatus.Invalid, null, error);
}
