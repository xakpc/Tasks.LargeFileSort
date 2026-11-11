using Xakpc.Tasks.LargeFileSort.Chunking;
using Xakpc.Tasks.LargeFileSort.Merging;

namespace Xakpc.Tasks.LargeFileSort.Tests;

/// <summary>
/// End-to-end tests of the whole sort: <see cref="ChunkBuilder"/> followed by <see cref="ChunkMerger"/>.
/// </summary>
[Trait("Category", "Integration")]
public class ExternalSortTests
{
    private const int Workers = 4;
    private const int DefaultChunkSize = 1024 * 1024;

    // a chunk size of 1 forces every line into its own chunk, so the merge does all the work
    private const int OneLinePerChunk = 1;

    [Theory]
    [InlineData(DefaultChunkSize)]
    [InlineData(OneLinePerChunk)]
    public async Task Sort_TaskExample_ReturnsExpectedOutput(int chunkSize)
    {
        // Arrange
        string[] input = ["415. Apple", "30432. Something something something", "1. Apple", "32. Cherry is the best", "2. Banana is yellow"];
        string[] expected = ["1. Apple", "415. Apple", "2. Banana is yellow", "32. Cherry is the best", "30432. Something something something"];

        // Act
        var (output, _) = await SortAsync(input, chunkSize);

        // Assert
        Assert.Equal(expected, output);
    }

    [Theory]
    [InlineData(DefaultChunkSize)]
    [InlineData(OneLinePerChunk)]
    public async Task Sort_DuplicateLinesAndNumbers_KeepsAllOfThem(int chunkSize)
    {
        // Arrange
        string[] input = ["3. Apple", "1. Banana", "3. Apple", "5. Banana", "1. Apple", "3. Apple"];
        string[] expected = ["1. Apple", "3. Apple", "3. Apple", "3. Apple", "1. Banana", "5. Banana"];

        // Act
        var (output, _) = await SortAsync(input, chunkSize);

        // Assert
        Assert.Equal(expected, output);
    }

    [Fact]
    public async Task Sort_UnusualButValidLines_WritesThemUnchanged()
    {
        // Arrange
        string[] input =
        [
            "2. Zebra",
            "1. Hello. World",
            "1.  padded ",
            "123456789012345678901234567890. Big",
            "007. Big",
        ];
        string[] expected =
        [
            "1.  padded ",
            "007. Big",
            "123456789012345678901234567890. Big",
            "1. Hello. World",
            "2. Zebra",
        ];

        // Act
        var (output, _) = await SortAsync(input);

        // Assert
        Assert.Equal(expected, output);
    }

    [Fact]
    public async Task Sort_MalformedLines_SkipsAndCountsThem()
    {
        // Arrange
        string[] input = ["no separator here", "2. Banana", "abc. not a number", "", "1. Apple"];

        // Act
        var (output, chunks) = await SortAsync(input);

        // Assert
        Assert.Equal(["1. Apple", "2. Banana"], output);
        Assert.Equal(2, chunks.LineCount);
        Assert.Equal(3, chunks.SkippedLines);
    }

    [Fact]
    public async Task Sort_EmptyFile_ReturnsEmptyOutput()
    {
        // Act
        var (output, chunks) = await SortAsync([]);

        // Assert
        Assert.Empty(output);
        Assert.Empty(chunks.Paths);
    }

    [Fact]
    public async Task Sort_RandomInput_MatchesInMemorySort()
    {
        // Arrange
        var random = new Random(42);
        string[] words = ["apple", "Apple", "banana", "cherry", "a", ""];
        var input = Enumerable.Range(0, 2_000)
            .Select(_ => $"{random.Next(0, 50)}. {words[random.Next(words.Length)]} {words[random.Next(words.Length)]}")
            .ToArray();
        var expected = input
            .OrderBy(l => l[(l.IndexOf(". ", StringComparison.Ordinal) + 2)..], StringComparer.Ordinal)
            .ThenBy(l => int.Parse(l[..l.IndexOf(". ", StringComparison.Ordinal)]))
            .ToArray();

        // Act
        var (output, chunks) = await SortAsync(input, chunkSize: 1_000);

        // Assert
        Assert.True(chunks.Paths.Count > 1, "the input should span several chunks");
        Assert.Equal(expected, output);
    }

    /// <summary>
    /// Runs the full sort pipeline (chunk + merge) on the given lines and returns the output lines.
    /// </summary>
    private static async Task<(string[] Output, SortedChunks Chunks)> SortAsync(
        string[] inputLines, int chunkSize = DefaultChunkSize)
    {
        using var temp = new TempDirectory();
        var inputFile = temp.File("input.txt");
        var outputFile = temp.File("output.txt");
        await File.WriteAllLinesAsync(inputFile, inputLines);

        using var workspace = new ChunkWorkspace();
        var chunks = new ChunkBuilder(workspace, Workers, chunkSize).Build(inputFile, null);
        ChunkMerger.Merge(chunks, outputFile, null);

        return (await File.ReadAllLinesAsync(outputFile), chunks);
    }
}
