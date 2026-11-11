using Xakpc.Tasks.LargeFileSort.Generator.Generation;

namespace Xakpc.Tasks.LargeFileSort.Tests.Generation;

[Trait("Category", "Integration")]
public class GeneratorTests
{
    private const string LinePattern = @"^\d+\. \S.*$";

    [Fact]
    public void Generate_SmallSize_WritesParseableLinesOfAtLeastRequestedSize()
    {
        // Arrange
        const long requestedSize = 10_000;
        using var temp = new TempDirectory();
        var outputFile = temp.File("generated.txt");
        var sut = new PhraseGenerator(["apple", "banana", "cherry"], outputFile, requestedSize);

        // Act
        var result = sut.Generate(null);

        // Assert
        var lines = File.ReadAllLines(outputFile);
        Assert.InRange(result.SizeBytes, requestedSize, requestedSize + 100); // overshoots by at most one line
        Assert.Equal(new FileInfo(outputFile).Length, result.SizeBytes);
        Assert.Equal(result.Lines, lines.Length);
        Assert.All(lines, line => Assert.Matches(LinePattern, line));
    }

    [Fact]
    public void Generate_NonAsciiWords_CountsSizeInBytes()
    {
        // Arrange
        using var temp = new TempDirectory();
        var outputFile = temp.File("generated.txt");
        var sut = new PhraseGenerator(["яблоко", "груша"], outputFile, 10_000);

        // Act
        var result = sut.Generate(null);

        // Assert
        Assert.Equal(new FileInfo(outputFile).Length, result.SizeBytes);
    }

    [Fact]
    public void Generate_ProducesRepeatedStringPartsAndUniqueNumbers()
    {
        // Arrange
        using var temp = new TempDirectory();
        var outputFile = temp.File("generated.txt");
        var words = Enumerable.Range(0, 1_000).Select(i => $"word{i}");
        var sut = new PhraseGenerator(words, outputFile, 200_000);

        // Act
        var result = sut.Generate(null);

        // Assert
        var parts = File.ReadAllLines(outputFile).Select(l => l.Split(". ", 2)).ToArray();
        var distinctTexts = parts.Select(p => p[1]).Distinct().Count();
        Assert.True(result.RepeatedLines > 0);
        Assert.True(distinctTexts < parts.Length, "some String parts must repeat");
        Assert.Equal(parts.Length, parts.Select(p => p[0]).Distinct().Count());
    }

    [Fact]
    public void Generate_SameInputs_ProducesSameFile()
    {
        // Arrange
        using var temp = new TempDirectory();
        string[] words = ["apple", "banana", "cherry"];
        var first = temp.File("first.txt");
        var second = temp.File("second.txt");

        // Act
        new PhraseGenerator(words, first, 5_000).Generate(null);
        new PhraseGenerator(words, second, 5_000).Generate(null);

        // Assert
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void Constructor_OnlyBlankWords_Throws()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new PhraseGenerator(["", "  "], "unused.txt", 100));
    }
}
