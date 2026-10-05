using MediaAcquisitionManager.Core.Abstractions.Metadata;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Api.Services.Catalog;

/// <summary>MAM-33 — the answer of a <see cref="CatalogLookupQuery"/>: the one catalog work it most likely names,
/// or nothing usable.</summary>
/// <param name="Kind">The work's kind; <c>null</c> when nothing usable was found.</param>
/// <param name="CatalogId">The work's catalog id; <c>null</c> when nothing usable was found.</param>
/// <param name="Evidence">What it matched by: <see cref="MatchEvidence.Identifier"/> for an IMDb id,
/// <see cref="MatchEvidence.TitleAndYear"/> for a title; <c>null</c> when nothing usable was found.</param>
public sealed record CatalogLookupResult(MediaWorkKind? Kind, string? CatalogId, MatchEvidence? Evidence)
{
    /// <summary>Nothing usable: no work, or more than one the lookup couldn't choose between.</summary>
    public static CatalogLookupResult Nothing { get; } = new(null, null, null);

    /// <summary>Whether a work was found.</summary>
    public bool IsFound => CatalogId is not null;
}
