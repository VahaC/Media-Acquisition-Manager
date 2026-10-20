using MediaAcquisitionManager.Api.Texts.Conversion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaAcquisitionManager.TestInfrastructure.Http;

/// <summary>
/// MAM-47 WP14 (ADR 0042, design §6) — a reason an older build stored in English, read in another language after the
/// upgrade: the English written over a message column as raw text, the way that build left it, and the startup
/// converter run over the database as the host's start runs it, so a test then reads the row through the HTTP API.
/// </summary>
public static class StoredEnglishHttpExtensions
{
    /// <summary>
    /// Writes <paramref name="english"/> into <paramref name="column"/> of the <paramref name="table"/> row whose
    /// <c>Id</c> is <paramref name="id"/>, as raw text — a sentence, not a message — then runs the startup converter
    /// over every message column, and returns what it did.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such row, or the converter didn't convert that text: the test
    /// planted words no template reproduces, and its later asserts would only mislead.</exception>
    public static async Task<StoredTextConversionResult> ConvertStoredEnglishAsync(
        this MediaAcquisitionManagerApiFactory factory, string table, string column, object id, string english)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // The table and column are the test's own names, never input; the words and the id travel as parameters.
        var update = $"UPDATE \"{table}\" SET \"{column}\" = {{0}} WHERE \"Id\" = {{1}}";
        var updated = 0;
        await factory.WithDbAsync(async db => updated = await db.Database.ExecuteSqlRawAsync(update, english, id));
        if (updated != 1)
            throw new InvalidOperationException($"{table} has no row {id} to plant the English in.");

        using var scope = factory.Services.CreateScope();
        var result = scope.ServiceProvider.GetRequiredService<StoredTextConverter>().Convert();
        if (result.Of(table, column) is not { Failed: false, Rows: [{ Outcome: ConversionOutcome.Converted }] })
            throw new InvalidOperationException($"The converter didn't convert the English planted in {table}.{column}: “{english}”.");
        return result;
    }
}
