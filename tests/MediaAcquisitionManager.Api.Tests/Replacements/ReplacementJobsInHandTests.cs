using MediaAcquisitionManager.Api.Services.Replacements;
using MediaAcquisitionManager.TestInfrastructure.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.Api.Tests.Replacements;

// MAM-26, ADR 0030 — the replacements someone in this process is writing to the media for: one taker per job until it lets
// go, anyone else turned away at once, and a job no one holds is what the mover's recovery takes for one a restart cut. One
// instance for the whole app, as only memory that dies with the process can tell a write under way from a cut one.
public sealed class ReplacementJobsInHandTests : IClassFixture<MediaAcquisitionManagerApiFactory>
{
    private readonly MediaAcquisitionManagerApiFactory _factory;

    public ReplacementJobsInHandTests(MediaAcquisitionManagerApiFactory factory) => _factory = factory;

    [Fact]
    public void TryTake_of_a_job_someone_holds_is_refused_until_it_is_released()
    {
        //Arrange
        var inHand = new ReplacementJobsInHand();
        var job = Guid.NewGuid();
        var other = Guid.NewGuid();

        //Act
        var first = inHand.TryTake(job);
        var second = inHand.TryTake(job);
        var another = inHand.TryTake(other);
        var heldBefore = inHand.Holds(job);
        inHand.Release(job);
        var heldAfter = inHand.Holds(job);
        var again = inHand.TryTake(job);

        //Assert
        Assert.Equal((true, false, true), (first, second, another));
        Assert.Equal((true, false, true), (heldBefore, heldAfter, again));
    }

    [Fact]
    public void Release_of_a_job_no_one_holds_changes_nothing()
    {
        //Arrange
        var inHand = new ReplacementJobsInHand();
        var job = Guid.NewGuid();

        //Act
        inHand.Release(job);

        //Assert
        Assert.False(inHand.Holds(job));
        Assert.True(inHand.TryTake(job));
    }

    [Fact]
    public void The_app_holds_one_registry_for_every_scope()
    {
        //Arrange
        var job = Guid.NewGuid();
        using var pass = _factory.Services.CreateScope();
        using var request = _factory.Services.CreateScope();

        //Act
        var taken = pass.ServiceProvider.GetRequiredService<ReplacementJobsInHand>().TryTake(job);
        var seen = request.ServiceProvider.GetRequiredService<ReplacementJobsInHand>().Holds(job);
        pass.ServiceProvider.GetRequiredService<ReplacementJobsInHand>().Release(job);

        //Assert
        // A singleton: the mover's pass and an owner's request, each in its own scope, see one another's jobs.
        Assert.Equal((true, true), (taken, seen));
    }
}
