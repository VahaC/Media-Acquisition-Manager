using MediaAcquisitionManager.Api.Texts.Conversion;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.TestInfrastructure.Replacements;

/// <summary>
/// MAM-47 (ADR 0042) — a reason or a problem a test plants in a replacement's or a removal's row, spelled as the English a
/// person reads: the message today's templates read it as, exactly what the app stores for that sentence and what the
/// startup converter stores for the same words written by an older build. The test keeps its English literal for what the
/// page answers, and the row holds key and values, never the words.
/// </summary>
/// <remarks>
/// Strict: words that read as no sentence of today's, or as several, fail the test that plants them — a planted row is
/// always one the app could have written.
/// </remarks>
public static class PlantedMessage
{
    /// <summary>The one message <paramref name="english"/> reads as (the stored-text parser, a 1000-character column).</summary>
    public static Message Of(string english)
    {
        var readings = MessageParser.Embedded.Parse(english, MessageJson.ColumnLength);
        return readings switch
        {
            [{ CutValue: false } reading] => reading.Message,
            [] => throw new ArgumentException($"No sentence of today's templates reads as \"{english}\".", nameof(english)),
            _ => throw new ArgumentException($"\"{english}\" reads as {readings.Count} sentences; plant the message itself.", nameof(english)),
        };
    }
}
