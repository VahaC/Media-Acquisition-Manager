using System.Globalization;
using MediaAcquisitionManager.Core.Library;
using MediaAcquisitionManager.Core.Matching;

namespace MediaAcquisitionManager.Core.Tests.Matching;

// MAM-12 — the form titles are compared in when a search or a release is matched to a library work:
// letter case, punctuation and spacing never make two titles, and the longest word of a key is a
// pre-filter that never leaves out a title the key matches.
public sealed class TitleMatchKeyTests
{
    [Theory]
    [InlineData("Spider-Man: No Way Home", "SPIDER MAN NO WAY HOME")]
    [InlineData("spider man   no way home", "SPIDER MAN NO WAY HOME")]
    [InlineData("Інтерстеллар", "ІНТЕРСТЕЛЛАР")]
    [InlineData("13-й район", "13 Й РАЙОН")]
    [InlineData("Face/Off", "FACE OFF")]
    [InlineData("2001: A Space Odyssey", "2001 A SPACE ODYSSEY")]
    [InlineData("WALL·E", "WALL E")]
    [InlineData("Amélie", "AMÉLIE")]
    [InlineData("  ...Interstellar!  ", "INTERSTELLAR")]
    [InlineData("Interstellar.2014_1080p", "INTERSTELLAR 2014 1080P")]
    [InlineData("!!!", "")]
    [InlineData("", "")]
    public void For_upper_cases_and_turns_every_run_of_other_characters_into_one_space(string title, string expected)
    {
        //Arrange

        //Act
        var key = TitleMatchKey.For(title);

        //Assert
        Assert.Equal(expected, key);
    }

    [Theory]
    // A decomposed "é": e and a combining acute (a non-spacing mark).
    [InlineData("Amélie", "AMÉLIE")]
    // Devanagari's vowel sign i is a spacing combining mark, its anusvara a non-spacing one.
    [InlineData("हिंदी", "हिंदी")]
    // A combining enclosing circle is an enclosing mark.
    [InlineData("A⃝ B", "A⃝ B")]
    public void For_keeps_a_combining_mark_with_its_letter(string title, string expected)
    {
        //Arrange

        //Act
        var key = TitleMatchKey.For(title);

        //Assert
        Assert.Equal(expected, key);
    }

    [Theory]
    // MAM-12 — Ukrainian writes its apostrophe three ways, and a source may have typed any of them: the
    // typewriter one, the right single quote, and the modifier letter its spelling recommends — a letter
    // (Lm), not punctuation, so as a word break it would have given the title a key of its own.
    [InlineData("Дев\u0027ять")]
    [InlineData("Дев\u2019ять")]
    [InlineData("Дев\u02BCять")]
    public void For_gives_a_title_one_key_whichever_apostrophe_it_is_written_with(string title)
    {
        //Arrange

        //Act
        var key = TitleMatchKey.For(title);

        //Assert
        Assert.Equal("ДЕВЯТЬ", key);
    }

    [Theory]
    // MAM-12 — a scene name leaves the apostrophe out ("Oceans.Eleven.2001"), so it joins the letters
    // around it rather than ending a word.
    [InlineData("Ocean's Eleven", "OCEANS ELEVEN")]
    [InlineData("Oceans.Eleven", "OCEANS ELEVEN")]
    [InlineData("Schindler\u2019s List", "SCHINDLERS LIST")]
    // Wherever it stands; a space beside it is still a word break.
    [InlineData("'Round Midnight", "ROUND MIDNIGHT")]
    [InlineData("Rock 'n' Roll", "ROCK N ROLL")]
    [InlineData("\u0027\u2019\u02BC", "")]
    public void For_drops_an_apostrophe_so_the_letters_around_it_join(string title, string expected)
    {
        //Arrange

        //Act
        var key = TitleMatchKey.For(title);

        //Assert
        Assert.Equal(expected, key);
    }

    /// <summary>Every apostrophe a key drops, one at a time.</summary>
    public static TheoryData<string> EveryApostrophe()
    {
        var apostrophes = new TheoryData<string>();
        foreach (var apostrophe in TitleMatchKey.Apostrophes)
            apostrophes.Add(apostrophe.ToString());
        return apostrophes;
    }

    [Theory]
    [MemberData(nameof(EveryApostrophe))]
    public void For_drops_every_apostrophe_it_lists(string apostrophe)
    {
        //Arrange
        var title = $"Ocean{apostrophe}s Eleven";

        //Act
        var key = TitleMatchKey.For(title);

        //Assert
        Assert.Equal("OCEANS ELEVEN", key);
    }

    [Fact]
    public void Apostrophes_are_the_eight_look_alikes_typed_for_one()
    {
        //Arrange
        // The typewriter apostrophe, the left and right single quotes, the modifier letters apostrophe and
        // turned comma, the grave and acute accents, and the full-width apostrophe.
        const string expected = "\u0027\u2018\u2019\u02BC\u02BB\u0060\u00B4\uFF07";

        //Act
        var apostrophes = TitleMatchKey.Apostrophes;

        //Assert
        Assert.Equal(expected, apostrophes);
    }

    [Fact]
    public void For_keeps_a_letter_outside_the_basic_plane_inside_its_word()
    {
        //Arrange
        // U+20000, a CJK ideograph written as two chars, neither of them a letter alone.
        const string title = "x\U00020000y z";

        //Act
        var key = TitleMatchKey.For(title);

        //Assert
        Assert.Equal("X\U00020000Y Z", key);
    }

    [Fact]
    public void For_does_not_depend_on_the_current_culture()
    {
        //Arrange
        // Turkish upper-cases i to İ; a key that depended on the machine would never match.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        string key;

        //Act
        try
        {
            key = TitleMatchKey.For("interstellar");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        //Assert
        Assert.Equal("INTERSTELLAR", key);
    }

    [Theory]
    [InlineData("Spider-Man: No Way Home")]
    [InlineData("Інтерстеллар / Interstellar")]
    [InlineData("13-й район")]
    [InlineData("Amélie (2001)")]
    [InlineData("ß straße")]
    [InlineData("Ocean's Eleven")]
    [InlineData("Дев\u02BCять / Rock 'n' Roll")]
    public void Every_word_of_a_key_is_in_the_stored_normalized_title_verbatim_once_its_apostrophes_are_removed(string title)
    {
        //Arrange
        // MAM-12 — the SQL pre-filter removes them from the stored copy, as a key drops them.
        var normalized = string.Concat(LibrarySearchText.Normalize(title).Where(character => !TitleMatchKey.Apostrophes.Contains(character)));

        //Act
        var words = TitleMatchKey.For(title).Split(' ');

        //Assert
        // What makes the library match's SQL pre-filter (NormalizedText, its apostrophes removed, contains
        // the longest word) safe.
        Assert.All(words, word => Assert.Contains(word, normalized, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SPIDER MAN NO WAY HOME", "SPIDER")]
    [InlineData("A BB CC", "BB")]
    [InlineData("13 Й РАЙОН", "РАЙОН")]
    [InlineData("INTERSTELLAR", "INTERSTELLAR")]
    public void LongestWord_is_the_first_of_the_longest_words(string key, string expected)
    {
        //Arrange

        //Act
        var word = TitleMatchKey.LongestWord(key);

        //Assert
        Assert.Equal(expected, word);
    }

    [Fact]
    public void LongestWord_of_an_empty_key_is_null()
    {
        //Arrange
        var key = TitleMatchKey.For("!!!");

        //Act
        var word = TitleMatchKey.LongestWord(key);

        //Assert
        Assert.Null(word);
    }
}
