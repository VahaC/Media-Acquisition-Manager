using MediaAcquisitionManager.Core.Matching;
using MediaAcquisitionManager.Core.Search;

namespace MediaAcquisitionManager.Core.Tests.Search;

// MAM-33, MAM-20 — what the name of a file no library reported claims to be: its file name without the extension, then
// its folder's name, each read as a release title is (ReleaseWorkClaims), and nothing from a name with no year
// (ADR 0012 — a title alone matches nothing here). The recommendations keep a work out by it; the Duplicates list offers
// the file as a possible match of a work by it.
public sealed class FileNameClaimsTests
{
    [Fact]
    public void Read_claims_by_the_file_name_then_by_the_folders_name()
    {
        //Arrange

        //Act
        var claims = FileNameClaims.Read("/media/movies/Чужі (1986)/Aliens.1986.1080p.BluRay.mkv");

        //Assert
        Assert.Equal(2, claims.Count);
        Assert.Equal(["Aliens"], claims[0].Titles);
        Assert.Equal(new ClaimedYears(1986, 1986), claims[0].Years);
        Assert.Empty(claims[0].Identifiers);
        Assert.Equal(["Чужі"], claims[1].Titles);
        Assert.Equal(new ClaimedYears(1986, 1986), claims[1].Years);
        Assert.Empty(claims[1].Identifiers);
    }

    [Theory]
    [InlineData("/media/movies/Aliens (1986)/aliens.mkv")]
    [InlineData(@"D:\Movies\Aliens (1986)\aliens.mkv")]
    public void Read_claims_by_the_folders_name_when_the_file_name_has_no_year(string path)
    {
        //Arrange

        //Act
        var claims = FileNameClaims.Read(path);

        //Assert
        var claim = Assert.Single(claims);
        Assert.Equal(["Aliens"], claim.Titles);
        Assert.Equal(new ClaimedYears(1986, 1986), claim.Years);
    }

    [Theory]
    [InlineData("/media/movies/Aliens/aliens.mkv")]
    [InlineData("/media/movies/Aliens/Aliens BDRip 1080p.mkv")]
    [InlineData("aliens.mkv")]
    [InlineData("")]
    [InlineData("/")]
    public void Read_claims_nothing_when_no_name_has_a_year(string path)
    {
        //Arrange

        //Act
        var claims = FileNameClaims.Read(path);

        //Assert
        Assert.Empty(claims);
    }

    [Fact]
    public void Read_of_a_path_with_only_a_name_claims_by_the_name_alone()
    {
        //Arrange

        //Act
        var claims = FileNameClaims.Read("Interstellar (2014).mkv");

        //Assert
        var claim = Assert.Single(claims);
        Assert.Equal(["Interstellar"], claim.Titles);
        Assert.Equal(new ClaimedYears(2014, 2014), claim.Years);
    }

    [Fact]
    public void Read_drops_the_extension_from_the_file_name()
    {
        //Arrange
        // The file name's last dotted part is its extension, read for nothing, even where it looks like a year: a scan
        // lists media files, which have one.
        const string path = "/media/movies/Interstellar.2014";

        //Act
        var claims = FileNameClaims.Read(path);

        //Assert
        Assert.Empty(claims);
    }

    [Theory]
    [InlineData("/media/tv/Severance (2022)/Season 01/Severance (2022) - S01E01 - Good News About Hell.mkv")]
    [InlineData("/media/downloads/Severance.S01.2022.1080p/Severance.S01E01.mkv")]
    [InlineData("/media/downloads/Severance.S01.2022.1080p/01 - Good News About Hell.mkv")]
    [InlineData("/media/downloads/Death Note (2006)/Death Note (E1-37 of 37) (2006).mkv")]
    [InlineData(@"D:\TV\Severance (2022)\Season 01\Severance (2022) - S01E02.mkv")]
    public void ReadUnlessEpisode_claims_nothing_for_a_file_or_folder_name_that_names_seasons_or_episodes(string path)
    {
        //Arrange

        //Act
        var claims = FileNameClaims.ReadUnlessEpisode(path);

        //Assert
        // MAM-20 — every episode of a show claims the show's title and year: by them, nine episodes would be one work.
        Assert.Empty(claims);
    }

    [Theory]
    [InlineData("/media/movies/Чужі (1986)/Aliens.1986.1080p.BluRay.mkv", 2)]
    [InlineData("/media/downloads/Aliens (1986)/aliens.mkv", 1)]
    [InlineData("/media/downloads/Aliens/aliens.mkv", 0)]
    public void ReadUnlessEpisode_of_a_film_claims_what_Read_claims(string path, int count)
    {
        //Arrange

        //Act
        var claims = FileNameClaims.ReadUnlessEpisode(path);

        //Assert
        Assert.Equal(count, claims.Count);
        Assert.Equal(
            FileNameClaims.Read(path).Select(claim => (string.Join('|', claim.Titles), claim.Years)),
            claims.Select(claim => (string.Join('|', claim.Titles), claim.Years)));
    }
}
