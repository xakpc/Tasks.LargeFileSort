using Xakpc.Tasks.LargeFileSort.Services;

namespace Xakpc.Tasks.LargeFileSort.Tests;

public class DataRowComparerTests
{
    [Fact]
    public void Sort_TaskExample_OrdersByTextThenNumber()
    {
        // Arrange
        var rows = Rows("415. Apple", "30432. Something something something", "1. Apple", "32. Cherry is the best", "2. Banana is yellow");
        string[] expected = ["1. Apple", "415. Apple", "2. Banana is yellow", "32. Cherry is the best", "30432. Something something something"];

        // Act
        rows.Sort(DataRowComparer.Instance);

        // Assert
        Assert.Equal(expected, rows.Select(r => r.Line));
    }

    [Theory]
    [InlineData("9. a", "10. a")]                                        // numeric, not lexicographic
    [InlineData("007. a", "8. a")]                                       // leading zeros ignored
    [InlineData("18446744073709551615. a", "18446744073709551616. a")]   // ulong.MaxValue vs ulong.MaxValue + 1
    [InlineData("99. a", "1. b")]                                        // text decides before number
    [InlineData("1. Zebra", "1. apple")]                                 // ordinal: uppercase before lowercase
    [InlineData("1. a", "1. a b")]                                       // prefix sorts first
    public void Compare_FirstBeforeSecond(string first, string second)
    {
        // Arrange
        var (x, y) = (Row(first), Row(second));

        // Act & Assert
        Assert.True(DataRowComparer.Instance.Compare(x, y) < 0);
        Assert.True(DataRowComparer.Instance.Compare(y, x) > 0);
    }

    [Theory]
    [InlineData("1. a", "1. a")]
    [InlineData("007. a", "7. a")]
    [InlineData("0. a", "000. a")]
    public void Compare_SameTextAndNumericValue_ReturnsZero(string first, string second)
    {
        // Act & Assert
        Assert.Equal(0, DataRowComparer.Instance.Compare(Row(first), Row(second)));
    }

    private static DataRow Row(string line) =>
        DataRow.TryParse(line, out var row) ? row : throw new ArgumentException($"Malformed test line '{line}'.");

    private static List<DataRow> Rows(params string[] lines) => lines.Select(Row).ToList();
}
