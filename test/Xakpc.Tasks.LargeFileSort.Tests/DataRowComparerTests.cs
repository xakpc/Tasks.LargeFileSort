using Xakpc.Tasks.LargeFileSort.Services;

namespace Xakpc.Tasks.LargeFileSort.Tests;

public class DataRowComparerTests
{
    [Fact]
    public void Compare_SameTextDifferentNumbers_OrdersByTextThenNumber()
    {
        // Arrange
        var rows = new List<DataRow>
        {
            new(415, "Apple"),
            new(30432, "Something something something"),
            new(1, "Apple"),
            new(32, "Cherry is the best"),
            new(2, "Banana is yellow"),
        };
        DataRow[] expected =
        [
            new(1, "Apple"),
            new(415, "Apple"),
            new(2, "Banana is yellow"),
            new(32, "Cherry is the best"),
            new(30432, "Something something something"),
        ];
        var comparer = new DataRowComparer();

        // Act
        rows.Sort(comparer);

        // Assert
        Assert.Equal(expected, rows);
    }
}
