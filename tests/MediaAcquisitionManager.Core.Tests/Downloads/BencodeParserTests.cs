using System.Text;
using MediaAcquisitionManager.Core.Downloads;

namespace MediaAcquisitionManager.Core.Tests.Downloads;

// MAM-38 — bencode (BEP 3) read into values whose bytes are slices of the file: integers, strings, lists and
// dictionaries in the order written, each with its encoded bytes; anything that isn't bencode, or nests deeper than
// libtorrent reads, is no value; bytes after the value are ignored, as libtorrent ignores them.
public sealed class BencodeParserTests
{
    [Fact]
    public void TryParse_reads_every_kind_and_keeps_each_values_encoded_bytes()
    {
        //Arrange
        var data = "d4:listli-3e3:abce4:spami42ee"u8.ToArray();

        //Act
        var parsed = BencodeParser.TryParse(data, out var value);

        //Assert
        Assert.True(parsed);
        Assert.Equal(BencodeKind.Dictionary, value!.Kind);
        Assert.Equal(["list", "spam"], value.Keys.Select(key => Encoding.ASCII.GetString(key.Span)));
        Assert.Equal(42, value.IntegerOf("spam"u8));
        var list = value.Find("list"u8)!;
        Assert.Equal(-3, list.Items[0].Integer);
        Assert.Equal("abc", Encoding.ASCII.GetString(list.Items[1].Bytes.Span));
        Assert.Equal("li-3e3:abce", Encoding.ASCII.GetString(list.Encoded.Span));
    }

    [Fact]
    public void TryParse_ignores_the_bytes_after_the_value()
    {
        //Arrange
        var data = "i7etrailing"u8.ToArray();

        //Act
        var parsed = BencodeParser.TryParse(data, out var value);

        //Assert
        Assert.True(parsed);
        Assert.Equal(7, value!.Integer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("i12")]
    [InlineData("i1x2e")]
    [InlineData("ie")]
    [InlineData("4:abc")]
    [InlineData("l")]
    [InlineData("d3:keyi1e")]
    [InlineData("di1ei2ee")]
    public void TryParse_of_anything_that_isnt_bencode_is_no_value(string data)
    {
        //Arrange
        var bytes = Encoding.ASCII.GetBytes(data);

        //Act
        var parsed = BencodeParser.TryParse(bytes, out var value);

        //Assert
        Assert.False(parsed);
        Assert.Null(value);
    }

    [Fact]
    public void TryParse_of_lists_nested_deeper_than_libtorrent_reads_is_no_value()
    {
        //Arrange
        var data = Encoding.ASCII.GetBytes(new string('l', 101) + new string('e', 101));

        //Act
        var parsed = BencodeParser.TryParse(data, out _);

        //Assert
        Assert.False(parsed);
    }
}
