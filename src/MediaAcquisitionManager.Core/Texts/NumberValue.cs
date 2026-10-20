namespace MediaAcquisitionManager.Core.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — a number, a count or a percentage (the sign belongs to the template). The renderer writes it in
/// the reader's language: English invariant and ungrouped, as today's sentences do; Ukrainian like the web app.
/// </summary>
public sealed record NumberValue : MessageValue
{
    /// <param name="number">The number as the sentence shows it: a producer rounds first, as today
    /// (<c>Math.Round(seconds, 2)</c>).</param>
    public NumberValue(decimal number) => Number = Normalise(number);

    /// <summary>
    /// The number without trailing fractional zeros (<c>1.0m</c> is <c>1m</c>), so the plural category and the digits are
    /// computed on the number as shown: "1 minute", not "1 minutes", and the stored JSON is one form per number.
    /// </summary>
    public decimal Number { get; }

    private static decimal Normalise(decimal number)
    {
        // Rounding to one digit fewer keeps the value exactly when that digit is a zero, and drops it from the scale.
        while (number.Scale > 0)
        {
            var shorter = decimal.Round(number, number.Scale - 1);
            if (shorter != number)
                break;
            number = shorter;
        }

        return number;
    }
}
