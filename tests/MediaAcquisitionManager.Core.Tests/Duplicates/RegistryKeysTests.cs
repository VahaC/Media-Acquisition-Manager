using MediaAcquisitionManager.Core.Abstractions.Media;
using MediaAcquisitionManager.Core.Duplicates;
using MediaAcquisitionManager.Core.Enums;

namespace MediaAcquisitionManager.Core.Tests.Duplicates;

// MAM-20 — the registry keys the Duplicates list joins two libraries' works by (ADR 0017, ADR 0028): an IMDb id is one
// key whatever the work's kind, as IMDb numbers films and episodes in one space; a TMDb or TVDB id is a key of the
// work's kind, as those registries number films, shows and episodes apart; an episode is also known by its show's ids
// with its season and episode. No library stores a film's and an episode's TMDb ids today (Jellyfin's sync drops an
// episode's), so the kind is pinned here rather than over HTTP.
public sealed class RegistryKeysTests
{
    [Fact]
    public void Of_keys_an_imdb_id_whatever_the_kind_and_another_registrys_id_within_the_works_kind()
    {
        //Arrange
        List<MediaIdentifier> identifiers =
        [
            new(MediaIdentifierScheme.Imdb, "tt0944947"),
            new(MediaIdentifierScheme.Tmdb, "1399"),
            new(MediaIdentifierScheme.Tvdb, "121361"),
        ];

        //Act
        var movie = RegistryKeys.Of(MediaWorkKind.Movie, identifiers, []);
        var episode = RegistryKeys.Of(MediaWorkKind.Episode, identifiers, []);

        //Assert
        Assert.Equal(
            [
                new RegistryKey(MediaIdentifierScheme.Imdb, "tt0944947", null, null, null),
                new RegistryKey(MediaIdentifierScheme.Tmdb, "1399", MediaWorkKind.Movie, null, null),
                new RegistryKey(MediaIdentifierScheme.Tvdb, "121361", MediaWorkKind.Movie, null, null),
            ],
            movie);
        Assert.Equal(
            [
                new RegistryKey(MediaIdentifierScheme.Imdb, "tt0944947", null, null, null),
                new RegistryKey(MediaIdentifierScheme.Tmdb, "1399", MediaWorkKind.Episode, null, null),
                new RegistryKey(MediaIdentifierScheme.Tvdb, "121361", MediaWorkKind.Episode, null, null),
            ],
            episode);
        // One key between them, the IMDb id's: a film's TMDb 1399 never meets an episode's.
        Assert.Equal([movie[0]], movie.Intersect(episode));
    }

    [Fact]
    public void Of_an_episode_keys_its_shows_ids_with_its_season_and_episode()
    {
        //Arrange
        DuplicateEpisodePlace place = new([new(MediaIdentifierScheme.TmdbTv, " 1399 "), new(MediaIdentifierScheme.Tvdb, "121361")], ["Game of Thrones"], 1, 1);

        //Act
        var keys = RegistryKeys.Of(MediaWorkKind.Episode, [], [place]);

        //Assert
        // Trimmed, of no kind: a series key is the show's id with the numbers, the same for every work at that place.
        Assert.Equal(
            [
                new RegistryKey(MediaIdentifierScheme.TmdbTv, "1399", null, 1, 1),
                new RegistryKey(MediaIdentifierScheme.Tvdb, "121361", null, 1, 1),
            ],
            keys);
        Assert.All(keys, key => Assert.True(key.IsSeriesKey));
    }

    [Fact]
    public void Of_gives_no_key_of_a_blank_id_and_each_key_once()
    {
        //Arrange
        List<MediaIdentifier> identifiers =
        [
            new(MediaIdentifierScheme.Imdb, "tt0090605"),
            new(MediaIdentifierScheme.Imdb, " TT0090605 "),
            new(MediaIdentifierScheme.Tmdb, "   "),
        ];

        //Act
        var keys = RegistryKeys.Of(MediaWorkKind.Movie, identifiers, []);

        //Assert
        Assert.Equal([new RegistryKey(MediaIdentifierScheme.Imdb, "tt0090605", null, null, null)], keys);
    }
}
