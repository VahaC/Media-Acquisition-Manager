using MediaAcquisitionManager.TestInfrastructure.Texts;
using Microsoft.Data.Sqlite;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — the pseudo pass's proof that the database keeps no sentence: a command carrying a rendered sentence,
// in its SQL or in a parameter, fails before it is sent; one carrying a message's JSON passes.
public sealed class PseudoWriteGuardTests
{
    [Fact]
    public void Refuse_lets_a_message_stored_as_its_json_through()
    {
        //Arrange
        using var command = new SqliteCommand("UPDATE \"DownloadJobs\" SET \"Error\" = @error");
        command.Parameters.AddWithValue("@error", """{"k":"downloads.send.failed"}""");

        //Act
        var exception = Record.Exception(() => PseudoWriteGuard.Refuse(command));

        //Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Refuse_fails_a_parameter_holding_a_rendered_sentence()
    {
        //Arrange
        using var command = new SqliteCommand("UPDATE \"DownloadJobs\" SET \"Error\" = @error");
        command.Parameters.AddWithValue("@error", "⟦The client refused the release.⟧");

        //Act
        var exception = Record.Exception(() => PseudoWriteGuard.Refuse(command));

        //Assert
        var refused = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("@error", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_fails_sql_holding_a_rendered_sentence()
    {
        //Arrange
        using var command = new SqliteCommand("UPDATE \"DownloadJobs\" SET \"Error\" = '⟦The client refused the release.⟧'");

        //Act
        var exception = Record.Exception(() => PseudoWriteGuard.Refuse(command));

        //Assert
        Assert.IsType<InvalidOperationException>(exception);
    }
}
