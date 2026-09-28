using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MediaAcquisitionManager.Infrastructure.Persistence;

/// <summary>
/// Applies the SQLite settings the app relies on to every connection the context opens, sync and
/// async alike.
/// </summary>
/// <remarks>
/// <para>
/// <c>journal_mode=WAL</c> lets readers (the readiness probe, the UI's polling) proceed while a
/// write is in flight instead of queueing behind it. The mode is stored in the file, but setting it
/// on every open also converts a database restored from an old backup the first time it is used.
/// </para>
/// <para>
/// <c>busy_timeout</c> and <c>foreign_keys</c> are per connection and forgotten on close, so they
/// can only be set here. With the first, SQLite waits for another connection's lock inside the step,
/// retrying within milliseconds of its release; without it the step returns <c>SQLITE_BUSY</c> at
/// once and Microsoft.Data.Sqlite retries it only every 150 ms. Neither gives up early:
/// Microsoft.Data.Sqlite keeps retrying a busy step until the command timeout (30 s by default), so
/// that, not this pragma, bounds how long a request waits for a stuck lock. Without the second a
/// delete could orphan rows on a SQLite build that does not enable foreign keys by default.
/// </para>
/// </remarks>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    /// <summary>
    /// How long SQLite itself waits for another writer's lock on each attempt. Writes in this app are
    /// short, so a wait this long is rare. It is not the request's overall limit: Microsoft.Data.Sqlite
    /// retries past it until the command timeout (30 s by default).
    /// </summary>
    private const string BusyTimeoutMilliseconds = "5000";

    private const string Pragmas =
        "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=" + BusyTimeoutMilliseconds + "; PRAGMA foreign_keys=ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
