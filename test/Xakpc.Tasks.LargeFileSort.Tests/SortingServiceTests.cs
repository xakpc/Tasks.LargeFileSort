using Xakpc.Tasks.LargeFileSort.Services;

namespace Xakpc.Tasks.LargeFileSort.Tests;

[Trait("Category", "Integration")]
public class SortingServiceTests
{
    private const string InputFileName = "input.txt";
    private const string OutputFileName = "output.txt";
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

    [Fact]
    public void BuildSortedChunks_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        using var temp = new TempDirectory();
        using var sut = new SortingService();

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => sut.BuildSortedChunks(temp.File("missing.txt"), null));
    }

    [Theory]
    [InlineData(new[] { "1. a", "2. b", "3. c", "4. d", "5. e" }, new[] { 2, 2, 1 })] // leftover row is written
    [InlineData(new[] { "1. a", "2. b", "3. c", "4. d" }, new[] { 2, 2 })]            // no empty extra chunk
    public async Task BuildSortedChunks_LineCountVsChunkSize_WritesExpectedChunks(
        string[] input, int[] expectedLinesPerChunk)
    {
        // Arrange
        // all lines have equal length; a chunk is cut once it reaches the size of two lines
        var twoLinesChunkSize = 2 * (input[0].Length + Environment.NewLine.Length);
        using var temp = new TempDirectory();
        var inputFile = temp.File(InputFileName);
        await File.WriteAllLinesAsync(inputFile, input);
        using var sut = new SortingService(twoLinesChunkSize);

        // Act
        var chunks = sut.BuildSortedChunks(inputFile, null);

        // Assert
        Assert.Equal(expectedLinesPerChunk, chunks.Paths.Select(c => File.ReadAllLines(c).Length));
        Assert.Equal(input, chunks.Paths.SelectMany(File.ReadAllLines));
    }

    [Fact]
    public async Task Dispose_DeletesWorkingDirectoryWithChunks()
    {
        // Arrange
        using var temp = new TempDirectory();
        var inputFile = temp.File(InputFileName);
        await File.WriteAllLinesAsync(inputFile, ["1. a"]);
        var sut = new SortingService();
        var chunks = sut.BuildSortedChunks(inputFile, null);

        // Act
        sut.Dispose();

        // Assert
        Assert.StartsWith(Path.GetTempPath(), sut.WorkingDirectory);
        Assert.False(Directory.Exists(sut.WorkingDirectory));
        Assert.All(chunks.Paths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void DeleteStaleWorkingDirectories_DeletesAbandonedAndKeepsRunning()
    {
        // Arrange
        var abandoned = Path.Combine(Path.GetTempPath(), $"LargeFileSort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "chunk-00000.txt"), "1. a");
        using var running = new SortingService();

        // Act
        var deleted = SortingService.DeleteStaleWorkingDirectories();

        // Assert
        Assert.True(deleted >= 1);
        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(running.WorkingDirectory));
    }

    /// <summary>
    /// Runs the full sort pipeline (chunk + merge) on the given lines and returns the output lines.
    /// </summary>
    private static async Task<(string[] Output, SortedChunks Chunks)> SortAsync(
        string[] inputLines, int chunkSize = DefaultChunkSize)
    {
        using var temp = new TempDirectory();
        var inputFile = temp.File(InputFileName);
        var outputFile = temp.File(OutputFileName);
        await File.WriteAllLinesAsync(inputFile, inputLines);

        using var sut = new SortingService(chunkSize);
        var chunks = sut.BuildSortedChunks(inputFile, null);
        sut.MergeChunks(chunks, outputFile, null);

        return (await File.ReadAllLinesAsync(outputFile), chunks);
    }
}
