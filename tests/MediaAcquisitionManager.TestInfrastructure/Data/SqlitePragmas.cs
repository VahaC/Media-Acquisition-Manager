using System.Globalization;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.TestInfrastructure.Data;

public static class SqlitePragmas
{
    /// <summary>
    /// The current value of <c>PRAGMA <paramref name="pragma"/></c> on the context's connection, as
    /// invariant text (<c>"wal"</c>, <c>"1"</c>, <c>"5000"</c>). The connection must already be open:
    /// the value belongs to that connection, and opening a new one would read a different one.
    /// </summary>
    public static async Task<string?> ReadAsync(ApplicationDbContext db, string pragma, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
