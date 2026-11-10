namespace Xakpc.Tasks.LargeFileSort.Tests;

[Trait("Category", "Integration")]
public class GeneratorTests
{
    private const string LinePattern = @"^\d+\. .+$";

    [Fact]
    public void Generate_SmallSize_WritesParseableLinesOfAtLeastRequestedSize()
    {
        // Arrange
        const ulong requestedSize = 10_000;
        string[] words = ["apple", "banana", "cherry"];
        using var temp = new TempDirectory();
        var outputFile = temp.File("generated.txt");
        var sut = new Generator.Services.PhraseGenerator(words, outputFile, requestedSize);

        // Act
        var result = sut.Generate(new Progress<double>());

        // Assert
        var lines = File.ReadAllLines(outputFile);
        Assert.True(new FileInfo(outputFile).Length >= (long)requestedSize);
        Assert.Equal(result.Lines, (ulong)lines.Length);
        Assert.All(lines, line => Assert.Matches(LinePattern, line));
    }
}
