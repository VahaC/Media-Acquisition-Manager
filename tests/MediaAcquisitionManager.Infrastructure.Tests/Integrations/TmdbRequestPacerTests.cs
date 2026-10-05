using System.Diagnostics;
using System.Net.Http.Headers;
using MediaAcquisitionManager.Infrastructure.Integrations.Tmdb;

namespace MediaAcquisitionManager.Infrastructure.Tests.Integrations;

// MAM-33 — the gate every TMDb request passes: turns 25 ms apart whoever takes them, a 429's hold-off for
// every caller, and the wait a Retry-After asks for, capped. Measured on the real monotonic clock, which
// is what the gate reads, so every bound below holds however slowly the machine runs.
public sealed class TmdbRequestPacerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Turns_taken_at_once_are_at_least_the_minimum_spacing_apart()
    {
        //Arrange
        var pacer = new TmdbRequestPacer(TimeProvider.System);
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Stopwatch.GetTimestamp();

        //Act
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => pacer.WaitTurnAsync(cancellationToken)));
        var elapsed = Stopwatch.GetElapsedTime(started);

        //Assert
        // The first turn starts at once and each later one 25 ms after the one before it.
        Assert.True(elapsed >= 4 * TmdbRequestPacer.MinimumSpacing, $"Five turns took {elapsed}.");
    }

    [Fact]
    public async Task A_hold_off_delays_the_next_turn_by_its_retry_after()
    {
        //Arrange
        var pacer = new TmdbRequestPacer(TimeProvider.System);
        var cancellationToken = TestContext.Current.CancellationToken;
        await pacer.WaitTurnAsync(cancellationToken);
        var started = Stopwatch.GetTimestamp();

        //Act
        pacer.HoldOff(new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(300)));
        await pacer.WaitTurnAsync(cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);

        //Assert
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(300), $"The turn came after {elapsed}.");
    }

    [Fact]
    public async Task A_shorter_hold_off_leaves_a_longer_one_running()
    {
        //Arrange
        var pacer = new TmdbRequestPacer(TimeProvider.System);
        var started = Stopwatch.GetTimestamp();

        //Act
        pacer.HoldOff(new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(300)));
        pacer.HoldOff(new RetryConditionHeaderValue(TimeSpan.Zero));
        await pacer.WaitTurnAsync(TestContext.Current.CancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);

        //Assert
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(300), $"The turn came after {elapsed}.");
    }

    [Fact]
    public async Task A_caller_that_goes_away_while_it_waits_gets_a_cancellation()
    {
        //Arrange
        var pacer = new TmdbRequestPacer(TimeProvider.System);
        pacer.HoldOff(new RetryConditionHeaderValue(TimeSpan.FromSeconds(10)));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        //Act
        var exception = await Record.ExceptionAsync(() => pacer.WaitTurnAsync(cancellation.Token));

        //Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public void RetryDelay_without_a_retry_after_is_two_seconds()
    {
        //Arrange

        //Act
        var delay = TmdbRequestPacer.RetryDelay(null, Now);

        //Assert
        Assert.Equal(TimeSpan.FromSeconds(2), delay);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    // Capped: a person is waiting on the page.
    [InlineData(11, 10)]
    [InlineData(3600, 10)]
    public void RetryDelay_of_seconds_is_those_seconds_at_most_ten(int seconds, int expectedSeconds)
    {
        //Arrange
        var retryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));

        //Act
        var delay = TmdbRequestPacer.RetryDelay(retryAfter, Now);

        //Assert
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(-30, 0)]
    [InlineData(3600, 10)]
    public void RetryDelay_of_a_date_is_the_time_until_it_at_most_ten_seconds(int secondsFromNow, int expectedSeconds)
    {
        //Arrange
        var retryAfter = new RetryConditionHeaderValue(Now.AddSeconds(secondsFromNow));

        //Act
        var delay = TmdbRequestPacer.RetryDelay(retryAfter, Now);

        //Assert
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
