using System.Net;
using MediaAcquisitionManager.Api.Contracts;
using MediaAcquisitionManager.Core.Enums;
using MediaAcquisitionManager.TestInfrastructure.Http;
using MediaAcquisitionManager.TestInfrastructure.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Seeders;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-40 — the Duplicates list's verdict on removing each file (GET /api/library/duplicates, LibraryDuplicateFile.removal):
// worked out from what the app has stored, by the rules the Remove request applies (each rule's sentence is
// CopyRemovalChecksHttpTests'), for every signed-in user alike — a member's page says first that only the owner removes —,
// and none on a possible match or one file's names, where nothing is removed. A clip of a disc folder is never removed one
// file at a time.
public sealed class CopyRemovalVerdictsHttpTests : IClassFixture<MediaAcquisitionManagerApiFactory>, IAsyncLifetime
{
    private const string AlienImdbId = "tt0078748";
    private const string KeptPath = "/media/movies/Alien (1979)/Alien (1979) 1080p.mkv";
    private const string RemovedPath = "/media/movies/Alien (1979)/Alien (1979) 2160p.mkv";
    private const string ClipPath = "/media/movies/Alien (1979) Disc/BDMV/STREAM/00001.m2ts";
    private const string DiscClipReason = "This file is part of a disc folder, which can't be removed one file at a time.";

    private readonly MediaAcquisitionManagerApiFactory _factory;
    private ReplacementVerificationHarness _harness = null!;

    public CopyRemovalVerdictsHttpTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    public async ValueTask InitializeAsync()
    {
        await _factory.ResetAsync();
        _harness = await ReplacementVerificationHarness.CreateAsync(_factory);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task List_lets_each_copy_of_one_work_be_removed_while_another_stays()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync(RemovedPath, cancellationToken);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken);

        //Assert
        var group = Assert.Single(list.Items);
        Assert.Equal(DuplicateClass.SameWork, group.Class);
        Assert.All(group.Files, file => Assert.Equal(new CopyRemovalVerdictDto(true, null), file.Removal));
    }

    [Fact]
    public async Task List_gives_a_member_the_same_verdicts_as_the_owner()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync(ClipPath, cancellationToken);
        var member = await new UserSeeder(_factory.Scope).CreateAuthorized("member");

        //Act
        var forOwner = Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken)).Items);
        var forMember = Assert.Single((await _factory.DuplicatesAsync(member.AccessToken, cancellationToken: cancellationToken)).Items);

        //Assert
        // The stored rules name no path, so a member reads the reason too; the page says first that only the owner removes.
        Assert.Equal(forOwner.Files.Select(file => file.Removal), forMember.Files.Select(file => file.Removal));
        Assert.Equal(new CopyRemovalVerdictDto(false, DiscClipReason), forMember.FileAt(ClipPath).Removal);
        Assert.Equal(new CopyRemovalVerdictDto(true, null), forMember.FileAt(KeptPath).Removal);
    }

    [Fact]
    public async Task Remove_of_a_disc_folders_clip_is_409_as_the_list_says()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await PlantCopiesAsync(ClipPath, cancellationToken);
        var group = Assert.Single((await _factory.DuplicatesAsync(_harness.Owner.AccessToken, cancellationToken: cancellationToken)).Items);

        //Act
        var response = await _factory.PostHttpRequest("/api/replacements/removals", _harness.Owner.AccessToken, group.RemoveRequestFor(ClipPath));

        //Assert
        Assert.Equal(new CopyRemovalVerdictDto(false, DiscClipReason), group.FileAt(ClipPath).Removal);
        Assert.Equal((HttpStatusCode.Conflict, DiscClipReason), (response.StatusCode, await response.ReadErrorAsync(cancellationToken)));
        Assert.Empty(await new CopyRemovalPlanter(_factory, _harness).RemovalsAsync(cancellationToken));
    }

    [Fact]
    public async Task List_gives_no_verdict_on_a_possible_match()
    {
        //Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        // Two works of one title and year, one without an id: maybe one film, so nothing is removed from them.
        await _harness.PlantFilmAsync("Halloween", 2018, "/media/movies/Halloween (2018)/Halloween (2018).mkv", inode: 11, imdbId: "tt1502407");
        await _harness.PlantFilmAsync("Halloween", 2018, "/media/movies/Halloween (2018) Extended/Halloween (2018) 2160p.mkv", inode: 12);
        await _harness.InspectAsync(cancellationToken);

        //Act
        var list = await _factory.DuplicatesAsync(_harness.Owner.AccessToken, "?class=Possible", cancellationToken);

        //Assert
        var group = Assert.Single(list.Items);
        Assert.Equal(DuplicateClass.Possible, group.Class);
        Assert.All(group.Files, file => Assert.Null(file.Removal));
    }

    /// <summary>Alien's copy at <see cref="KeptPath"/> and another at <paramref name="otherPath"/>, both in the ticked Фільми,
    /// inspected.</summary>
    private async Task PlantCopiesAsync(string otherPath, CancellationToken cancellationToken)
    {
        await _harness.PlantFilmAsync("Alien", 1979, KeptPath, inode: 1, imdbId: AlienImdbId);
        await _harness.PlantFilmAsync("Alien", 1979, otherPath, inode: 2, imdbId: AlienImdbId);
        await _harness.InspectAsync(cancellationToken);
    }
}
