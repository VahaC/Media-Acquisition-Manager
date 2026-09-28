using MediaAcquisitionManager.Core.Options;
using MediaAcquisitionManager.Core.Storage;

namespace MediaAcquisitionManager.Core.Tests.Storage;

public sealed class StorageLayoutTests
{
    // Pure path arithmetic — nothing here touches the disk. Rooted in the temp path so the same
    // test holds on Windows (C:\…) and Unix (/tmp/…).
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "mam-content-root");

    [Fact]
    public void Resolve_with_default_options_uses_Data_under_the_content_root()
    {
        //Arrange
        var options = new StorageOptions();

        //Act
        var layout = StorageLayout.Resolve(options, ContentRoot);

        //Assert
        Assert.Equal(Path.Combine(ContentRoot, "Data"), layout.DataDirectory);
    }

    [Fact]
    public void Resolve_with_a_relative_directory_resolves_it_against_the_content_root()
    {
        //Arrange
        var options = new StorageOptions { DataDirectory = "nested/state" };

        //Act
        var layout = StorageLayout.Resolve(options, ContentRoot);

        //Assert
        Assert.Equal(Path.Combine(ContentRoot, "nested", "state"), layout.DataDirectory);
    }

    [Fact]
    public void Resolve_with_a_parent_relative_directory_returns_it_normalised()
    {
        //Arrange
        var options = new StorageOptions { DataDirectory = "../shared/state" };

        //Act
        var layout = StorageLayout.Resolve(options, ContentRoot);

        //Assert
        Assert.Equal(Path.Combine(Path.GetDirectoryName(ContentRoot)!, "shared", "state"), layout.DataDirectory);
    }

    [Fact]
    public void Resolve_with_an_absolute_directory_uses_it_as_is()
    {
        //Arrange
        var absolute = Path.Combine(Path.GetTempPath(), "mam-elsewhere", "data");
        var options = new StorageOptions { DataDirectory = absolute };

        //Act
        var layout = StorageLayout.Resolve(options, ContentRoot);

        //Assert
        Assert.Equal(absolute, layout.DataDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_with_a_blank_directory_falls_back_to_the_default(string configured)
    {
        //Arrange
        var options = new StorageOptions { DataDirectory = configured };

        //Act
        var layout = StorageLayout.Resolve(options, ContentRoot);

        //Assert
        Assert.Equal(Path.Combine(ContentRoot, StorageOptions.DefaultDataDirectory), layout.DataDirectory);
    }

    [Fact]
    public void Resolve_puts_the_database_and_the_secrets_inside_the_data_directory()
    {
        //Arrange
        var options = new StorageOptions { DataDirectory = "state" };

        //Act
        var layout = StorageLayout.Resolve(options, ContentRoot);

        //Assert
        Assert.Equal(Path.Combine(layout.DataDirectory, "mam.db"), layout.DatabasePath);
        Assert.Equal(Path.Combine(layout.DataDirectory, ".secrets"), layout.SecretsDirectory);
    }
}
