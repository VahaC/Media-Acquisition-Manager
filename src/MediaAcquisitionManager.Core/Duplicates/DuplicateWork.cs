using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Duplicates;

/// <summary>MAM-20 — a library work holding file names, as <see cref="DuplicateGrouping"/> reads it.</summary>
/// <param name="WorkId">The work.</param>
/// <param name="Kind">What the library says it is: only a <see cref="MediaWorkKind.Movie"/> or an
/// <see cref="MediaWorkKind.Episode"/> holds files.</param>
/// <param name="Keys">Its registry keys (<see cref="RegistryKeys.Of"/>), from its ids as every reader counts them
/// (MAM-34 links applied).</param>
/// <param name="Titles">Its titles, every source's, with their years.</param>
/// <param name="Places">Where it sits in a show — an episode's positions, an episode-to-show link's — the places its
/// series keys were made of.</param>
public sealed record DuplicateWork(
    Guid WorkId,
    MediaWorkKind Kind,
    IReadOnlyList<RegistryKey> Keys,
    IReadOnlyList<ReportedTitle> Titles,
    IReadOnlyList<DuplicateEpisodePlace> Places);
