using Xakpc.Tasks.LargeFileSort.Services;

namespace Xakpc.Tasks.LargeFileSort.Tests;

[Trait("Category", "Integration")]
public class SortingServiceTests
{
    private const string InputFileName = "input.txt";
    private const string OutputFileName = "output.txt";
    private const int DefaultChunkSize = 1024 * 1024;

    private static readonly string[] SampleInput =
    [
        "415. Apple",
        "30432. Something something something",
        "1. Apple",
        "32. Cherry is the best",
        "2. Banana is yellow",
    ];

    private static readonly string[] SampleSorted =
    [
        "1. Apple",
        "415. Apple",
        "2. Banana is yellow",
        "32. Cherry is the best",
        "30432. Something something something",
    ];

    [Fact]
    public async Task SortAsync_SampleInput_ReturnsSortedLines()
    {
        // Act
        var actual = await SortAsync(SampleInput);

        // Assert
        Assert.Equal(SampleSorted, actual);
    }

    [Fact]
    public async Task SortAsync_TextContainsDotSpace_PreservesFullText()
    {
        // Arrange
        const string lineWithDotInText = "1. Hello. World";
        const string plainLine = "2. Zebra";

        // Act
        var actual = await SortAsync([plainLine, lineWithDotInText]);

        // Assert
        Assert.Equal([lineWithDotInText, plainLine], actual);
    }

    [Fact]
    public async Task SortAsync_MalformedLines_SkipsThem()
    {
        // Arrange
        const string noSeparator = "no separator here";
        const string notANumber = "abc. not a number";
        const string validLine = "1. Apple";

        // Act
        var actual = await SortAsync([noSeparator, notANumber, validLine]);

        // Assert
        Assert.Equal([validLine], actual);
    }

    [Fact]
    public async Task SortAsync_EmptyFile_ReturnsEmptyOutput()
    {
        // Act
        var actual = await SortAsync([]);

        // Assert
        Assert.Empty(actual);
    }

    [Fact]
    public async Task SortAsync_DuplicateTextAcrossChunks_ReturnsGloballySortedLines()
    {
        // Arrange
        // chunk size of 1 byte forces every line into its own chunk, so the merge does all the work
        const int oneLinePerChunk = 1;
        string[] input = ["3. Apple", "1. Banana", "2. Apple", "5. Banana", "4. Apple"];
        string[] expected = ["2. Apple", "3. Apple", "4. Apple", "1. Banana", "5. Banana"];

        // Act
        var actual = await SortAsync(input, oneLinePerChunk);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task BuildSortedChunksAsync_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        using var temp = new TempDirectory();
        using var sut = new SortingService(temp.Path);
        var missingFile = temp.File("missing.txt");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.BuildSortedChunksAsync(missingFile, null));
    }

    [Theory]
    [InlineData(new[] { "1. a", "2. b", "3. c", "4. d", "5. e" }, new[] { 2, 2, 1 })] // leftover row is written
    [InlineData(new[] { "1. a", "2. b", "3. c", "4. d" }, new[] { 2, 2 })]            // no empty extra chunk
    public async Task BuildSortedChunksAsync_LineCountVsChunkSize_WritesExpectedChunks(
        string[] input, int[] expectedLinesPerChunk)
    {
        // Arrange
        // all lines have equal length; a chunk is cut once it reaches the size of two lines
        var twoLinesChunkSize = 2 * (input[0].Length + Environment.NewLine.Length);
        using var temp = new TempDirectory();
        var inputFile = temp.File(InputFileName);
        await File.WriteAllLinesAsync(inputFile, input);
        using var sut = new SortingService(temp.Path, twoLinesChunkSize);

        // Act
        var chunks = await sut.BuildSortedChunksAsync(inputFile, null);

        // Assert
        Assert.Equal(expectedLinesPerChunk, chunks.Select(c => File.ReadAllLines(c).Length));
        Assert.Equal(input, chunks.SelectMany(File.ReadAllLines));
    }

    /// <summary>
    /// Runs the full sort pipeline (chunk + merge) on the given lines and returns the output lines.
    /// </summary>
    private static async Task<string[]> SortAsync(string[] inputLines, int chunkSize = DefaultChunkSize)
    {
        using var temp = new TempDirectory();
        var inputFile = temp.File(InputFileName);
        var outputFile = temp.File(OutputFileName);
        await File.WriteAllLinesAsync(inputFile, inputLines);

        using var sut = new SortingService(temp.Path, chunkSize);
        var chunks = await sut.BuildSortedChunksAsync(inputFile, null);
        await sut.MergeChunksAsync(chunks, outputFile);

        return await File.ReadAllLinesAsync(outputFile);
    }
}
