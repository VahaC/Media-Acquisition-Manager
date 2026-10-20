using System.Globalization;
using MediaAcquisitionManager.Api.Texts;

namespace MediaAcquisitionManager.Api.Tests.Texts;

// MAM-47 (ADR 0042) — English byte-identical to today's sentences; Ukrainian as the web app writes it. The Ukrainian
// pins are what node 24.18.1 printed (re-run for MAM-47 on 2026-10-19) for
//   new Intl.NumberFormat('uk', { maximumFractionDigits: 20 }).format(n)
// — every significant digit, as the server's numbers are already rounded by their producer — with U+00A0 between groups
// from 1,000 up and a decimal comma. English grouped is today's N0 ("1,234,567 bytes").
public sealed class LanguageFormatsTests
{
    private const string Nbsp = " ";

    [Theory]
    [InlineData("7", "7", "7", "7")]
    [InlineData("1234", "1234", "1,234", "1" + Nbsp + "234")]
    [InlineData("1234567", "1234567", "1,234,567", "1" + Nbsp + "234" + Nbsp + "567")]
    [InlineData("0.5", "0.5", "0.5", "0,5")]
    [InlineData("0.0017", "0.0017", "0.0017", "0,0017")]
    [InlineData("45.6", "45.6", "45.6", "45,6")]
    [InlineData("1000", "1000", "1,000", "1" + Nbsp + "000")]
    [InlineData("12345", "12345", "12,345", "12" + Nbsp + "345")]
    [InlineData("100000", "100000", "100,000", "100" + Nbsp + "000")]
    [InlineData("-1234.5", "-1234.5", "-1,234.5", "-1" + Nbsp + "234,5")]
    [InlineData("1234567.891", "1234567.891", "1,234,567.891", "1" + Nbsp + "234" + Nbsp + "567,891")]
    [InlineData("999", "999", "999", "999")]
    public void Numbers_are_written_as_pinned(string number, string english, string englishGrouped, string ukrainian)
    {
        //Arrange
        var value = decimal.Parse(number, CultureInfo.InvariantCulture);

        //Act
        var written = (
            LanguageFormats.Number(value, TextLanguage.English),
            LanguageFormats.GroupedNumber(value, TextLanguage.English),
            LanguageFormats.Number(value, TextLanguage.Ukrainian),
            LanguageFormats.GroupedNumber(value, TextLanguage.Ukrainian),
            LanguageFormats.Number(value, TextLanguage.Pseudo));

        //Assert
        Assert.Equal((english, englishGrouped, ukrainian, ukrainian, english), written);
    }

    [Theory]
    [InlineData(2, TextLanguage.English, "Two")]
    [InlineData(3, TextLanguage.English, "Three")]
    [InlineData(10, TextLanguage.English, "Ten")]
    [InlineData(11, TextLanguage.English, "11")]
    [InlineData(1, TextLanguage.English, "1")]
    [InlineData(0, TextLanguage.English, "0")]
    [InlineData(3, TextLanguage.Pseudo, "Three")]
    [InlineData(3, TextLanguage.Ukrainian, "3")]
    [InlineData(1234, TextLanguage.Ukrainian, "1" + Nbsp + "234")]
    public void WordNumber_spells_two_to_ten_in_english_only(int number, TextLanguage language, string expected)
    {
        //Arrange

        //Act
        var written = LanguageFormats.WordNumber(number, language);

        //Assert
        Assert.Equal(expected, written);
    }

    [Fact]
    public void WordNumber_writes_digits_for_a_fraction()
    {
        //Arrange

        //Act
        var written = LanguageFormats.WordNumber(2.5m, TextLanguage.English);

        //Assert
        Assert.Equal("2.5", written);
    }

    [Theory]
    [InlineData(6_990_000L, "1:56:30")]
    [InlineData(2_700_000L, "0:45:00")]
    [InlineData(360_000_000L, "100:00:00")]
    [InlineData(999L, "0:00:00")]
    [InlineData(1_999L, "0:00:01")]
    [InlineData(0L, "0:00:00")]
    public void Duration_is_the_servers_clock_truncated(long milliseconds, string expected)
    {
        //Arrange

        //Act
        var written = LanguageFormats.Duration(milliseconds);

        //Assert
        Assert.Equal(expected, written);
    }

    [Theory]
    [InlineData(8_388_608L, "common.size.mib", 8L)]
    [InlineData(4_194_304L, "common.size.mib", 4L)]
    [InlineData(1_572_864L, "common.size.kib", 1536L)]
    [InlineData(1_000L, "common.size.bytes", 1000L)]
    [InlineData(1L, "common.size.bytes", 1L)]
    [InlineData(0L, "common.size.mib", 0L)]
    public void SizeUnit_names_the_exact_unit_as_DescribeBytes_did(long bytes, string key, long count)
    {
        //Arrange

        //Act
        var unit = LanguageFormats.SizeUnit(bytes);

        //Assert
        Assert.Equal((key, count), unit);
    }
}
