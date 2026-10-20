using System.Globalization;
using System.Text;

namespace MediaAcquisitionManager.Api.Texts;

/// <summary>
/// MAM-47 (ADR 0042) — how each language writes a number, a size's number and a running time (Strategy per language).
/// English is byte-identical to today's sentences per call site; Ukrainian is what the web app's <c>lib/format.ts</c>
/// prints through <c>Intl.NumberFormat('uk')</c> (pinned against node in LanguageFormatsTests).
/// </summary>
/// <remarks>Hand-coded rather than through <see cref="CultureInfo"/>: the Windows dev box, the Linux image and their ICU
/// versions then can't disagree about a separator. The pseudo-language writes English's.</remarks>
public static class LanguageFormats
{
    /// <summary>Ukrainian's group separator, as <c>Intl</c> writes it: a no-break space, from 1,000 up.</summary>
    public const char UkrainianGroupSeparator = ' ';

    private const int MebiByte = 1024 * 1024;
    private const int KibiByte = 1024;

    /// <summary>The English words of <c>number:word</c> for 2 to 10 — today's count at a sentence's start.</summary>
    private static readonly string[] EnglishCountWords = ["Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten"];

    /// <summary>
    /// <c>number</c>: English as today's invariant interpolation — no grouping, every significant digit (the producer
    /// rounds first: <c>2.5</c>, <c>0.0017</c>, <c>1234</c>); Ukrainian grouped from 1,000 with a no-break space and a
    /// decimal comma, every significant digit (<c>1 234</c>, <c>0,0017</c>).
    /// </summary>
    public static string Number(decimal number, TextLanguage language) => language == TextLanguage.Ukrainian
        ? Write(number, UkrainianGroupSeparator, ',')
        : number.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>number:grouped</c>: English grouped with commas from 1,000 (today's <c>N0</c> byte counts: <c>1,234,567</c>);
    /// Ukrainian as <see cref="Number"/>.
    /// </summary>
    public static string GroupedNumber(decimal number, TextLanguage language) => language == TextLanguage.Ukrainian
        ? Write(number, UkrainianGroupSeparator, ',')
        : Write(number, ',', '.');

    /// <summary>
    /// <c>number:word</c>: English spells 2 to 10 at a sentence's start ("Two new files are…"), digits otherwise;
    /// Ukrainian writes digits, since «Два/Дві» would need the noun's gender.
    /// </summary>
    public static string WordNumber(decimal number, TextLanguage language) =>
        language != TextLanguage.Ukrainian && number is >= 2 and <= 10 && number.Scale == 0
            ? EnglishCountWords[(int)number - 2]
            : Number(number, language);

    /// <summary>
    /// <c>duration</c>: the server's clock in every language — hours always shown and unbounded, minutes and seconds
    /// in two digits, truncated (<c>1:56:30</c>, <c>0:45:00</c>, <c>100:00:00</c>).
    /// </summary>
    public static string Duration(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}");
    }

    /// <summary>
    /// <c>size</c>: the exact unit, as today's <c>DescribeBytes</c> — whole MiB, else whole KiB, else bytes — as the
    /// key of the unit's counted template (<c>common.size.mib</c>, <c>common.size.kib</c>, <c>common.size.bytes</c>) and
    /// the count it takes.
    /// </summary>
    public static (string Key, long Count) SizeUnit(long bytes) => bytes switch
    {
        _ when bytes % MebiByte == 0 => ("common.size.mib", bytes / MebiByte),
        _ when bytes % KibiByte == 0 => ("common.size.kib", bytes / KibiByte),
        _ => ("common.size.bytes", bytes),
    };

    private static string Write(decimal number, char groupSeparator, char decimalSeparator)
    {
        // Invariant decimal formatting never uses an exponent and keeps every digit of the scale.
        var invariant = number.ToString(CultureInfo.InvariantCulture);
        var negative = invariant.StartsWith('-');
        var digits = negative ? invariant[1..] : invariant;
        var point = digits.IndexOf('.');
        var integer = point < 0 ? digits : digits[..point];

        var builder = new StringBuilder(invariant.Length + integer.Length / 3);
        if (negative)
            builder.Append('-');
        for (var index = 0; index < integer.Length; index++)
        {
            if (index > 0 && (integer.Length - index) % 3 == 0)
                builder.Append(groupSeparator);
            builder.Append(integer[index]);
        }

        if (point >= 0)
            builder.Append(decimalSeparator).Append(digits, point + 1, digits.Length - point - 1);
        return builder.ToString();
    }
}
