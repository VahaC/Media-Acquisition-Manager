namespace MediaAcquisitionManager.Api.Services.Recommendations;

/// <summary>MAM-33 — the outcome of <see cref="IRecommendationPosterService.GetAsync"/>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Content">The poster's bytes when <see cref="RecommendationPosterStatus.Served"/>.</param>
/// <param name="MediaType">Their media type then: <c>image/jpeg</c> or <c>image/png</c>, by the file name's
/// extension.</param>
/// <param name="Error">The sentence when <see cref="RecommendationPosterStatus.Failed"/>.</param>
public sealed record RecommendationPosterResult(RecommendationPosterStatus Status, byte[]? Content, string? MediaType, string? Error)
{
    public static RecommendationPosterResult NotFound { get; } = new(RecommendationPosterStatus.NotFound, null, null, null);

    public static RecommendationPosterResult Served(byte[] content, string mediaType) =>
        new(RecommendationPosterStatus.Served, content, mediaType, null);

    public static RecommendationPosterResult Failed(string error) => new(RecommendationPosterStatus.Failed, null, null, error);
}
