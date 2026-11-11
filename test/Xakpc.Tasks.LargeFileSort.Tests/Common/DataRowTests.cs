using Xakpc.Tasks.LargeFileSort.Common;

namespace Xakpc.Tasks.LargeFileSort.Tests.Common;

public class DataRowTests
{
    [Theory]
    [InlineData("415. Apple", "415", "Apple")]
    [InlineData("1. Hello. World", "1", "Hello. World")]                 // only the first ". " separates
    [InlineData("007. Bond", "007", "Bond")]                             // leading zeros kept verbatim
    [InlineData("1.  padded ", "1", " padded ")]                         // text whitespace kept verbatim
    [InlineData("1. ", "1", "")]                                         // empty text is allowed
    [InlineData("123456789012345678901234567890. Big", "123456789012345678901234567890", "Big")] // > ulong
    public void TryParse_ValidLine_SplitsNumberAndTextAndKeepsLine(string line, string number, string text)
    {
        // Act
        var parsed = DataRow.TryParse(line, out var row);

        // Assert
        Assert.True(parsed);
        Assert.Equal(line, row.Line);
        Assert.Equal(number, row.Number.ToString());
        Assert.Equal(text, row.Text.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("no separator here")]
    [InlineData("abc. not a number")]
    [InlineData(". no number")]
    [InlineData("1.no space")]
    [InlineData("-1. negative")]
    [InlineData("+1. signed")]
    [InlineData(" 1. leading space")]
    [InlineData("1 2. inner space")]
    [InlineData("١. arabic-indic digit")]
    public void TryParse_MalformedLine_ReturnsFalse(string line)
    {
        // Act
        var parsed = DataRow.TryParse(line, out _);

        // Assert
        Assert.False(parsed);
    }
}
