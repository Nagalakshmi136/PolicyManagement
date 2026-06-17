using FluentAssertions;
using PolicyManagement.Domain.Common;

namespace PolicyManagement.Domain.Tests.Common;

public class PagedResultTests
{
    // ------------------------------------------------------------------
    // HasNextPage
    // ------------------------------------------------------------------

    [Fact]
    public void HasNextPage_WhenMoreItemsExist_ShouldReturnTrue()
    {
        // Arrange
        var result = new PagedResult<int>(new[] { 1, 2, 3 }, TotalCount: 10, Page: 1, PageSize: 3);

        // Assert
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void HasNextPage_WhenOnLastPage_ShouldReturnFalse()
    {
        // Arrange
        var result = new PagedResult<int>(new[] { 10 }, TotalCount: 10, Page: 4, PageSize: 3);

        // Assert
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void HasNextPage_WhenTotalCountEqualsPageSize_ShouldReturnFalse()
    {
        // Arrange — exactly one full page
        var result = new PagedResult<int>(new[] { 1, 2, 3 }, TotalCount: 3, Page: 1, PageSize: 3);

        // Assert
        result.HasNextPage.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // HasPreviousPage
    // ------------------------------------------------------------------

    [Fact]
    public void HasPreviousPage_WhenOnFirstPage_ShouldReturnFalse()
    {
        // Arrange
        var result = new PagedResult<int>(new[] { 1 }, TotalCount: 5, Page: 1, PageSize: 2);

        // Assert
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_WhenOnSecondPage_ShouldReturnTrue()
    {
        // Arrange
        var result = new PagedResult<int>(new[] { 3, 4 }, TotalCount: 5, Page: 2, PageSize: 2);

        // Assert
        result.HasPreviousPage.Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // TotalPages
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(10, 3, 4)]   // 10 / 3 = 3.33 → ceiling = 4
    [InlineData(9, 3, 3)]    // exactly 3 pages
    [InlineData(1, 20, 1)]   // fewer items than page size
    [InlineData(0, 10, 0)]   // empty result set
    [InlineData(100, 10, 10)]
    public void TotalPages_ShouldBeCorrectCeiling(int totalCount, int pageSize, int expectedPages)
    {
        // Arrange
        var result = new PagedResult<int>(Array.Empty<int>(), totalCount, Page: 1, pageSize);

        // Assert
        result.TotalPages.Should().Be(expectedPages);
    }

    [Fact]
    public void TotalPages_WhenPageSizeIsZero_ShouldReturnZero()
    {
        // Arrange
        var result = new PagedResult<int>(Array.Empty<int>(), TotalCount: 5, Page: 1, PageSize: 0);

        // Assert
        result.TotalPages.Should().Be(0);
    }
}
