using MediaAcquisitionManager.Core.Abstractions.Files;
using MediaAcquisitionManager.Core.Paths;
using MediaAcquisitionManager.Infrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Files;

namespace MediaAcquisitionManager.Infrastructure.Tests.Files;

// MAM-18 — whether the resolver itself refused a path's links or the file system couldn't answer now: the walk of the
// media folders refuses a link the resolver refused (nothing under it is a name of the media folders) and keeps what
// was found under one that leads nowhere now (ADR 0021). Over real links in a temp folder; a link to a folder is a
// junction on Windows, which needs no privilege.
public sealed class LinkResolverRefusedTests : IDisposable
{
    private readonly TemporaryFolder _folder = new("mam-link-resolver-");

    private readonly string _media;

    public LinkResolverRefusedTests() => _media = _folder.CreateFolder("media");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Resolve_of_two_links_leading_to_each_other_is_refused()
    {
        //Arrange
        var first = Path.Combine(_media, "loop-a");
        var second = Path.Combine(_media, "loop-b");
        FileLinks.CreateFolderLink(first, second);
        FileLinks.CreateFolderLink(second, first);
        var path = Parse(first);

        //Act
        var result = LinkResolver.Resolve(path, path.ToString(), out var refused);

        //Assert
        Assert.True(refused);
        Assert.Equal(
            (FileInventoryStatus.Unreadable, null, $"“{path}” leads through more than 40 links, likely a loop, so the app doesn't follow them."),
            (result.Status, result.Value, result.Error));
    }

    [Fact]
    public void Resolve_of_a_link_that_leads_nowhere_is_not_refused()
    {
        //Arrange
        // A link to a disk that is away: the file system has no answer now, which isn't the resolver refusing it.
        var link = Path.Combine(_media, "gone");
        FileLinks.CreateFolderLink(link, _folder.PathOf("nowhere"));
        var path = Parse(link);

        //Act
        var result = LinkResolver.Resolve(path, path.ToString(), out var refused);

        //Assert
        Assert.False(refused);
        Assert.Equal((FileInventoryStatus.NotFound, null, $"There is no file at “{path}”."), (result.Status, result.Value, result.Error));
    }

    [Fact]
    public void Resolve_of_a_link_inside_the_folder_is_found_and_not_refused()
    {
        //Arrange
        var real = _folder.CreateFolder("media", "real");
        var link = Path.Combine(_media, "linked");
        FileLinks.CreateFolderLink(link, real);
        var path = Parse(link);

        //Act
        var result = LinkResolver.Resolve(path, path.ToString(), out var refused);

        //Assert
        Assert.False(refused);
        Assert.Equal((FileInventoryStatus.Found, Parse(real).ToString(), null), (result.Status, result.Value?.ToString(), result.Error));
    }

    private static NormalizedPath Parse(string path) =>
        NormalizedPath.TryParse(path, out var parsed, out _) ? parsed : throw new ArgumentException($"“{path}” is not a full path.", nameof(path));
}
