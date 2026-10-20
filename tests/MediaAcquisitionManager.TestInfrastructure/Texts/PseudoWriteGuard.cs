using System.Data.Common;
using MediaAcquisitionManager.Api.Texts;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaAcquisitionManager.TestInfrastructure.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — the pseudo pass's proof that the database keeps no sentence: a command whose SQL or any string
/// parameter holds a pseudo-language mark fails before it is sent. Outside a request the server renders in the
/// pseudo-language in that pass, so a background writer that stored a rendered sentence instead of its message is caught
/// in the test that drove it.
/// </summary>
/// <remarks>
/// Attached by the factory to the context's options in the pseudo pass only, the way it attaches its other command
/// interceptor — no seam in the app. Failing the command itself, not a sweep of the tables after the test: a sweep costs
/// a scan per test and blames whichever test runs next.
/// </remarks>
public sealed class PseudoWriteGuard : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Refuse(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Refuse(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Refuse(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Refuse(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Refuse(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Refuse(command);
        return ValueTask.FromResult(result);
    }

    /// <exception cref="InvalidOperationException">The command would store or compare a rendered sentence.</exception>
    public static void Refuse(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (PseudoText.HasMark(command.CommandText))
            throw new InvalidOperationException(
                $"{PseudoText.FailurePrefix} a database command holds a rendered sentence ({MessageRenderer.PseudoOpen}…{MessageRenderer.PseudoClose}); a column keeps the message, never its words. SQL: {command.CommandText}");

        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.Value is string text && PseudoText.HasMark(text))
                throw new InvalidOperationException(
                    $"{PseudoText.FailurePrefix} the database parameter {parameter.ParameterName} holds a rendered sentence, \"{text}\"; a column keeps the message, never its words. SQL: {command.CommandText}");
        }
    }
}
