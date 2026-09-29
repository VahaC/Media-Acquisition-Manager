using MediaAcquisitionManager.Core.Paging;

namespace MediaAcquisitionManager.Core.Tests.Paging;

public sealed class PagingRulesTests
{
    // Spelled out rather than read from PagingRules: they are what every list answers (MAM-4's
    // UsersHttpTests pin them over HTTP too).
    private const string PageError = "Page must be 1 or more.";
    private const string PageSizeError = "Page size must be between 1 and 100.";

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Validate_of_a_page_below_1_is_the_rule(int page)
    {
        //Arrange

        //Act
        var error = PagingRules.Validate(page, 20);

        //Assert
        Assert.Equal(PageError, error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void Validate_of_a_page_size_outside_1_to_100_is_the_rule(int pageSize)
    {
        //Arrange

        //Act
        var error = PagingRules.Validate(1, pageSize);

        //Assert
        Assert.Equal(PageSizeError, error);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(int.MaxValue, 100)]
    public void Validate_at_the_bounds_is_valid(int page, int pageSize)
    {
        //Arrange

        //Act
        var error = PagingRules.Validate(page, pageSize);

        //Assert
        Assert.Null(error);
    }

    [Fact]
    public void Validate_checks_the_page_before_the_page_size()
    {
        //Arrange

        //Act
        var error = PagingRules.Validate(0, 0);

        //Assert
        Assert.Equal(PageError, error);
    }
}
