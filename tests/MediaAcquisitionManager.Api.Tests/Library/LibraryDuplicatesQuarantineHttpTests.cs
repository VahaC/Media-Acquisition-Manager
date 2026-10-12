using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Files;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;

namespace MediaAcquisitionManager.Api.Tests.Library;

// MAM-40 — a name in the quarantine counts in no duplicate group (GET /api/library/duplicates), whoever took it there: a
// replacement's or a removal's name moving, in the quarantine, on its way back or out, or deleted for good has left the
// library, whatever Jellyfin's last sync and the app's last inspection still list at its path. It is matched by its path and
// the identity it was moved with, so it counts again once put back, and a new file put at its path counts as it should.
public sealed class LibraryDuplicatesQuarantineHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string KeptPath = "/media/movies/Alien (1979)/Alien (1979) 1080p.mkv";
    private const string MovedPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public LibraryDuplicatesQuarantineHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
        // Two copies of Alien, inspected: a group of one work's copies until one of them leaves.
        await _harness.PlantFilmAsync("Alien", 1979, KeptPath, inode: 1, imdbId: AlienImdbId);
        await _harness.PlantFilmAsync("Alien", 1979, MovedPath, inode: 2, imdbId: AlienImdbId);
        await _harness.InspectAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [InlineData(ReplacementNameState.Moving)]
    [InlineData(ReplacementNameState.Quarantined)]
    [InlineData(ReplacementNameState.Restoring)]
    [InlineData(ReplacementNameState.Deleting)]
    [InlineData(ReplacementNameState.Deleted)]
    public async Task List_counts_no_name_a_replacement_took_into_the_quarantine(ReplacementNameState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new ReplacementQuarantinePlanter(_factory, _harness).PlantAsync(
            ReplacementJobState.Quarantined, [new PlantedName(ReplacementNameKind.Video, MovedPath, TestMedia.File(inode: 2), state)]);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal((0, 0), (list.TotalCount, list.Items.Count));
    }

    [Theory]
    [InlineData(ReplacementNameState.Planned)]
    [InlineData(ReplacementNameState.Stayed)]
    [InlineData(ReplacementNameState.Restored)]
    [InlineData(ReplacementNameState.Lost)]
    public async Task List_counts_a_name_that_is_still_or_again_where_it_was(ReplacementNameState state)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new CopyRemovalPlanter(_factory, _harness).PlantAsync(
            CopyRemovalState.NeedsReview, [new PlantedName(ReplacementNameKind.Video, MovedPath, TestMedia.File(inode: 2), state)]);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal([KeptPath, MovedPath], Assert.Single(list.Items).Files.Select(file => file.File.AppPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_counts_a_new_file_given_the_inode_of_a_name_deleted_for_good_at_its_path(bool anotherSize)
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // The removed copy was deleted for good with another size or time than the file at its path now (inode 2, as
        // inspected), which the disk gave the freed inode when the film was downloaded again under the same name.
        var now = TestMedia.File(inode: 2);
        var deleted = anotherSize ? now with { SizeBytes = now.SizeBytes - 1 } : now with { ModifiedUtc = now.ModifiedUtc.AddTicks(-1) };
        await new CopyRemovalPlanter(_factory, _harness).PlantAsync(
            CopyRemovalState.Deleted, [new PlantedName(ReplacementNameKind.Video, MovedPath, deleted, ReplacementNameState.Deleted)]);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        var group = Assert.Single(list.Items);
        Assert.Equal([(KeptPath, (long?)1), (MovedPath, (long?)2)], group.Files.Select(file => (file.File.AppPath!, file.Inode)));
    }

    [Fact]
    public async Task List_counts_no_name_deleted_for_good_while_its_path_identity_size_and_time_all_match()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new CopyRemovalPlanter(_factory, _harness).PlantAsync(
            CopyRemovalState.Deleted, [new PlantedName(ReplacementNameKind.Video, MovedPath, TestMedia.File(inode: 2), ReplacementNameState.Deleted)]);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        Assert.Equal((0, 0), (list.TotalCount, list.Items.Count));
    }

    [Fact]
    public async Task List_counts_a_new_file_put_at_a_quarantined_names_path()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await new CopyRemovalPlanter(_factory, _harness).PlantAsync(
            CopyRemovalState.Quarantined, [new PlantedName(ReplacementNameKind.Video, MovedPath, TestMedia.File(inode: 2))]);
        // Another file at the removed copy's old path since, which the next inspection read.
        _factory.Files.AddFile(MovedPath, TestMedia.File(inode: 7), TestMedia.Taken());
        await _harness.InspectAsync(cancellationToken);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        var group = Assert.Single(list.Items);
        Assert.Equal([(KeptPath, (long?)1), (MovedPath, (long?)7)], group.Files.Select(file => (file.File.AppPath!, file.Inode)));
    }
}
