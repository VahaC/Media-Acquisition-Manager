using MediaAcquisitionManager.TestInfrastructure.Texts;

namespace MediaAcquisitionManager.Infrastructure.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — no test of this project compares a message with a string (<see cref="MessageComparisons"/>):
/// <c>result.Error == "…"</c> still compiles once a result's type is a <c>Message</c>, and is always false, so the check it
/// meant never runs. Api.Tests' <c>NoLiteralSentenceTests</c> holds the same rule over the source assemblies, its own
/// tests and the harness, and the guard that the rule flags a planted comparison.
/// </summary>
public sealed class NoMessageComparedWithAStringTests
{
    [Fact]
    public void No_test_compares_a_message_with_a_string()
    {
        //Arrange
        var scan = IlLiteralScanner.Scan(typeof(NoMessageComparedWithAStringTests).Assembly);

        //Act
        var offenders = MessageComparisons.WithAStringIn([scan]);

        //Assert
        Assert.Contains(typeof(NoMessageComparedWithAStringTests), scan.Types);
        Assert.Empty(scan.Unresolved);
        Assert.True(
            offenders.Count == 0,
            $"A message compared with == or != against a string meets it as a text value through the implicit conversion, so it is always unequal — compare keys (message.Key == …), whole messages with Equals, or TestTexts.En(message) with the English:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }
}
