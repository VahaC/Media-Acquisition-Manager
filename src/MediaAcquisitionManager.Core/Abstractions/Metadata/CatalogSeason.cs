namespace MediaAcquisitionManager.Core.Abstractions.Metadata;

/// <summary>MAM-33 — one season of a show as the source lists it.</summary>
/// <param name="Number">The season's number; 0 for the specials.</param>
/// <param name="AirDate">When its first episode aired; <c>null</c> when the source doesn't know, as for a
/// season still to come.</param>
/// <param name="EpisodeCount">How many episodes the source lists for it.</param>
public sealed record CatalogSeason(int Number, DateOnly? AirDate, int EpisodeCount);
