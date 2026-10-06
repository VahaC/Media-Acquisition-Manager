using MediaAcquisitionManager.Core.Library;

namespace MediaAcquisitionManager.Core.Tests.Library;

// MAM-34, R1 — which links whose item is gone move, decided as a whole: a link moves only to the one item its files lead
// to, when no other link's files lead there and the item holds no link; anything else moves none of the links involved,
// whatever order they come in.
public sealed class LinkReattachmentTests
{
    private static readonly Guid First = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Moves_takes_each_link_to_the_one_item_its_files_lead_to()
    {
        //Arrange
        var unattached = new[] { (First, Holders("b")), (Second, Holders("c")) };

        //Act
        var moves = LinkReattachment.Moves(unattached, Holders());

        //Assert
        Assert.Equal(new Dictionary<Guid, string> { [First] = "b", [Second] = "c" }, moves);
    }

    [Fact]
    public void Moves_leaves_a_link_whose_files_lead_to_no_item_or_to_two()
    {
        //Arrange
        var unattached = new[] { (First, Holders()), (Second, Holders("b", "c")) };

        //Act
        var moves = LinkReattachment.Moves(unattached, Holders());

        //Assert
        Assert.Empty(moves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Moves_takes_no_link_to_an_item_another_link_leads_to_whichever_comes_first(bool reversed)
    {
        //Arrange
        // The second link's files are under "b" and "c": "b" is not the first link's to take either.
        (Guid, IReadOnlySet<string>)[] unattached = [(First, Holders("b")), (Second, Holders("b", "c"))];
        if (reversed)
            Array.Reverse(unattached);

        //Act
        var moves = LinkReattachment.Moves(unattached, Holders());

        //Assert
        Assert.Empty(moves);
    }

    [Fact]
    public void Moves_takes_no_link_to_an_item_that_holds_a_link()
    {
        //Arrange
        var unattached = new[] { (First, Holders("b")) };

        //Act
        var moves = LinkReattachment.Moves(unattached, Holders("b"));

        //Assert
        Assert.Empty(moves);
    }

    [Fact]
    public void Moves_compares_item_ids_byte_for_byte()
    {
        //Arrange
        // Ids differing in letter case are two items: the link's item "B" is not "b", which holds a link.
        var unattached = new[] { (First, Holders("B")) };

        //Act
        var moves = LinkReattachment.Moves(unattached, Holders("b"));

        //Assert
        Assert.Equal(new Dictionary<Guid, string> { [First] = "B" }, moves);
    }

    private static IReadOnlySet<string> Holders(params string[] items) => items.ToHashSet(StringComparer.Ordinal);
}
