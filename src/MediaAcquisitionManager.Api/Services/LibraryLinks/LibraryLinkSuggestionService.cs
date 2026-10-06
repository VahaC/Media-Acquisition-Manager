using MediaAcquisitionManager.Api.Services.Catalog;
using MediaAcquisitionManager.Api.Services.Library;
using MediaAcquisitionManager.Core.Abstractions;
using MediaAcquisitionManager.Core.Abstractions.Integrations;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Api.Services.LibraryLinks;

/// <inheritdoc cref="ILibraryLinkSuggestionService"/>
/// <remarks>
/// Split by facet: what the facets share here; the free search and the pasted id in
/// <c>LibraryLinkSuggestionService.Find.cs</c>, the review queue, "none fits", the confident list and "Accept all
/// confident" in <c>.Review.cs</c>.
/// </remarks>
public sealed partial class LibraryLinkSuggestionService(
    ApplicationDbContext db,
    ILibraryLinkSuggestionReader suggestions,
    ILibraryLinkService links,
    ILibraryWorkReader workReader,
    ICatalogConnector catalogs,
    ICatalogStore store,
    TimeProvider time,
    ILogger<LibraryLinkSuggestionService> logger) : ILibraryLinkSuggestionService
{
    /// <summary>The longest part of a request echoed back in a sentence.</summary>
    private const int EchoLength = 64;

    private const string KindMissingError = "Choose a TMDb film (Movie) or show (Series).";

    private static string KindInvalidError(string kind) => $"“{Echo(kind)}” is not a TMDb kind. Use Movie or Series.";

    /// <summary>A film (<c>Movie</c>) or a show (<c>Series</c>), any case; <c>null</c> for anything else — an episode is
    /// never a catalog work of its own (ADR 0017).</summary>
    private static MediaWorkKind? CatalogKindOf(string text) =>
        text.Equals(nameof(MediaWorkKind.Movie), StringComparison.OrdinalIgnoreCase) ? MediaWorkKind.Movie
        : text.Equals(nameof(MediaWorkKind.Series), StringComparison.OrdinalIgnoreCase) ? MediaWorkKind.Series
        : null;

    /// <summary>The works the owner marked "none fits", of <paramref name="workIds"/> or of all.</summary>
    private async Task<HashSet<Guid>> NoneFitsAsync(IReadOnlyCollection<Guid>? workIds, CancellationToken cancellationToken)
    {
        var searches = db.LibraryLinkSearches.AsNoTracking().Where(search => search.NoneFitsUtc != null);
        if (workIds is not null)
        {
            List<Guid> ids = [.. workIds];
            searches = searches.Where(search => EF.Parameter(ids).Contains(search.MediaWorkId));
        }

        return [.. await searches.Select(search => search.MediaWorkId).ToListAsync(cancellationToken)];
    }

    /// <summary>A catalog call's failure as the owner reads it: masked again with the call's secrets, a second pass after
    /// the adapter's own (the MAM-7 rule).</summary>
    private static string Masked<T>(IntegrationCallResult<T> result, CatalogConnection catalog) where T : class =>
        SecretRedaction.Redact(result.Error!, catalog.Settings.Secrets)!;

    private static string Echo(string value) =>
        value.Length <= EchoLength ? value : string.Concat(value.AsSpan(0, EchoLength - 1), "…");
}
