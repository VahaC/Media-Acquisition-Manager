using MediaAcquisitionManager.Core.Abstractions.Library;
using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.Core.Replacements;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-25 — the inputs of <see cref="ReplacementPairing"/> as the verifier builds them from a job's rows: matched works,
/// ticked copies and new files, the library's readings of their items, and the titles the works and episodes go by now.
/// Alien (1979) and Aliens (1986) for films, The Penguins of Madagascar for episodes, with their real registry ids. A test
/// passes only works it ticks a copy of, as the verifier does: the job's rows hold no other (MAM-24's planner writes only
/// those), so another film of a pack is another work to the pairing, never a matched one.
/// </summary>
public static class ReplacementPairingInputs
{
    /// <summary>The library's integration name the sentences quote.</summary>
    public const string LibraryName = "Jellyfin";

    /// <summary>Alien (1979): TMDb 348, IMDb tt0078748.</summary>
    public static readonly ReplacementPairingWork Alien = Film(
        new Guid("a1000000-0000-0000-0000-000000000001"), "Alien (1979)", ["Alien"], Id(MediaIdentifierScheme.Tmdb, "348"), Id(MediaIdentifierScheme.Imdb, "tt0078748"));

    /// <summary>Aliens (1986): TMDb 679, IMDb tt0090605.</summary>
    public static readonly ReplacementPairingWork Aliens = Film(
        new Guid("a1000000-0000-0000-0000-000000000002"), "Aliens (1986)", ["Aliens"], Id(MediaIdentifierScheme.Tmdb, "679"), Id(MediaIdentifierScheme.Imdb, "tt0090605"));

    /// <summary>The Penguins of Madagascar: TMDb TV 1044, TVDB 80703, IMDb tt0892700.</summary>
    public static readonly ReplacementPairingWork Penguins = Show(
        new Guid("b1000000-0000-0000-0000-000000000001"),
        "The Penguins of Madagascar",
        Id(MediaIdentifierScheme.TmdbTv, "1044"),
        Id(MediaIdentifierScheme.Tvdb, "80703"),
        Id(MediaIdentifierScheme.Imdb, "tt0892700"));

    /// <summary>The series item id the library gives the Penguins' episodes.</summary>
    public const string PenguinsItemId = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";

    /// <summary>A registry id.</summary>
    public static MediaIdentifier Id(MediaIdentifierScheme scheme, string value) => new(scheme, value);

    /// <summary>A matched film.</summary>
    public static ReplacementPairingWork Film(Guid workId, string name, IReadOnlyList<string> titles, params MediaIdentifier[] identifiers) =>
        new(workId, MediaWorkKind.Movie, name, titles, identifiers);

    /// <summary>A matched show, its title its name.</summary>
    public static ReplacementPairingWork Show(Guid workId, string name, params MediaIdentifier[] identifiers) =>
        new(workId, MediaWorkKind.Series, name, [name], identifiers);

    /// <summary>A film item the library finished reading, with <paramref name="identifiers"/>.</summary>
    public static ReplacementLibraryReading FilmItem(params MediaIdentifier[] identifiers) =>
        new(LibraryFileKind.Movie, true, identifiers, null, [], null, null, null);

    /// <summary>
    /// An episode item the library finished reading at <paramref name="season"/> and <paramref name="episode"/>, ending at
    /// <paramref name="end"/> when it reports an end, in the Penguins' series unless <paramref name="seriesIdentifiers"/>
    /// say otherwise.
    /// </summary>
    public static ReplacementLibraryReading EpisodeItem(int? season, int? episode, int? end = null, IReadOnlyList<MediaIdentifier>? seriesIdentifiers = null) =>
        new(LibraryFileKind.Episode, true, [], PenguinsItemId, seriesIdentifiers ?? Penguins.Identifiers, season, episode, end);

    /// <summary>An item that is no work: an extra.</summary>
    public static ReplacementLibraryReading OtherItem(bool refreshed = true) => new(LibraryFileKind.Other, refreshed, [], null, [], null, null, null);

    /// <summary>A new file at <paramref name="appPath"/>, at <paramref name="position"/> in the client's list.</summary>
    public static ReplacementPairingNewFile NewFile(int position, string appPath, ReplacementLibraryReading library, long? durationMilliseconds = 6_990_000) =>
        new(Guid.NewGuid(), position, appPath, library, durationMilliseconds);

    /// <summary>A ticked copy of a film, at <paramref name="position"/> in the job's list; its item isn't read.</summary>
    public static ReplacementPairingOldCopy FilmCopy(int position, ReplacementPairingWork film, string appPath, long? durationMilliseconds = null) =>
        new(Guid.NewGuid(), position, film.WorkId, null, null, [], appPath, null, durationMilliseconds);

    /// <summary>
    /// A ticked copy of the Penguins' episode at <paramref name="season"/> and <paramref name="episode"/> as scheduled, its
    /// item read as <paramref name="library"/> (by default the same episode, finished), going by
    /// <paramref name="episodeTitles"/> (by default none).
    /// </summary>
    public static ReplacementPairingOldCopy EpisodeCopy(
        int position,
        int? season,
        int? episode,
        string appPath,
        ReplacementLibraryReading? library = null,
        long? durationMilliseconds = null,
        IReadOnlyList<string>? episodeTitles = null) =>
        new(Guid.NewGuid(), position, Penguins.WorkId, season, episode, episodeTitles ?? [], appPath, library ?? EpisodeItem(season, episode), durationMilliseconds);
}
