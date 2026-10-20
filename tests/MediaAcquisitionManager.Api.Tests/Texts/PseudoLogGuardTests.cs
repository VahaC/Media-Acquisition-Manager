using MediaAcquisitionManager.TestInfrastructure.Logging;
using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Extensions.Logging;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — logs stay English: in the pseudo pass the recording provider fails an entry that holds a rendered
// sentence, so the code that logged it fails in the test that drove it; in the normal pass it only records. An entry that
// reports another check of the pass failing (EF logs the write guard's exception, marked value and all) never fails, so
// that failure reaches the test instead of being hidden under this one.
public sealed class PseudoLogGuardTests
{
    [Fact]
    public void Log_of_a_rendered_sentence_fails_in_the_pseudo_pass_only()
    {
        //Arrange
        var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger("Probe");

        //Act
        var exception = Record.Exception(() => logger.LogWarning("Moved: {Reason}", "⟦The app stopped.⟧"));

        //Assert
        Assert.Equal(TestLanguage.IsPseudo, exception is InvalidOperationException);
        Assert.Equal("Moved: ⟦The app stopped.⟧", Assert.Single(provider.Entries).Message);
    }

    [Fact]
    public void Log_of_english_never_fails()
    {
        //Arrange
        var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger("Probe");

        //Act
        var exception = Record.Exception(() => logger.LogWarning("Moved: {Reason}", "The app stopped."));

        //Assert
        Assert.Null(exception);
        Assert.Single(provider.Entries);
    }

    [Fact]
    public void Log_that_reports_another_pseudo_pass_failure_never_fails()
    {
        //Arrange
        var provider = new RecordingLoggerProvider();
        var logger = provider.CreateLogger("Microsoft.EntityFrameworkCore.Update");
        var failure = new InvalidOperationException(
            $"{PseudoText.FailurePrefix} the database parameter @p0 holds a rendered sentence, \"⟦The app stopped.⟧\".");
        var saving = new InvalidOperationException("An error occurred while saving the entity changes.", failure);

        //Act
        var exception = Record.Exception(() => logger.LogError(saving, "Saving failed:{NewLine}{Exception}", Environment.NewLine, saving));

        //Assert
        Assert.Null(exception);
        Assert.Contains("⟦The app stopped.⟧", Assert.Single(provider.Entries).Message, StringComparison.Ordinal);
    }
}
