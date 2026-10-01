using MediaAcquisitionManager.Api.Contracts;

namespace MediaAcquisitionManager.Api.Services.Releases;

/// <summary>The outcome of <see cref="IReleaseSearchService.SearchAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Response">What every source answered when <see cref="ReleaseSearchStatus.Searched"/>.</param>
/// <param name="Error">The sentence saying what the search doesn't take otherwise.</param>
public sealed record ReleaseSearchResult(ReleaseSearchStatus Status, ReleaseSearchResponse? Response, string? Error)
{
    public static ReleaseSearchResult Searched(ReleaseSearchResponse response) => new(ReleaseSearchStatus.Searched, response, null);

    public static ReleaseSearchResult Invalid(string error) => new(ReleaseSearchStatus.Invalid, null, error);
}
