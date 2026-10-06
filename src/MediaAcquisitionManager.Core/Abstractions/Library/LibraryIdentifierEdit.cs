using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Abstractions.Library;

/// <summary>MAM-34 — one id of a <see cref="LibraryIdentifierChange"/>.</summary>
/// <param name="Scheme">The registry: a film takes <see cref="MediaIdentifierScheme.Tmdb"/> and
/// <see cref="MediaIdentifierScheme.Imdb"/>, a show <see cref="MediaIdentifierScheme.TmdbTv"/> and
/// <see cref="MediaIdentifierScheme.Imdb"/> — each only on a kind of item the writer's
/// <see cref="IMediaLibraryIdentifierWriter.WritableKinds"/> holds (Jellyfin's: films and shows).</param>
/// <param name="Value">The id to hold; <c>null</c> removes the key.</param>
/// <param name="Rule">When the edit applies.</param>
/// <param name="Expected">For <see cref="LibraryIdentifierEditRule.OnlyWhereExpected"/>: the value the item must hold
/// now (compared trimmed, in any letter case); <c>null</c> means the item must hold none.</param>
public sealed record LibraryIdentifierEdit(MediaIdentifierScheme Scheme, string? Value, LibraryIdentifierEditRule Rule, string? Expected = null);
