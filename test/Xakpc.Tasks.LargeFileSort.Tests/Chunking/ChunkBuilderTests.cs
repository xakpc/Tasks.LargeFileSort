using Xakpc.Tasks.LargeFileSort.Chunking;

namespace Xakpc.Tasks.LargeFileSort.Tests.Chunking;

[Trait("Category", "Integration")]
public class ChunkBuilderTests
{
    private const int Workers = 4;

    [Theory]
    [InlineData(new[] { "1. a", "2. b", "3. c", "4. d", "5. e" }, new[] { 2, 2, 1 })] // leftover row is written
    [InlineData(new[] { "1. a", "2. b", "3. c", "4. d" }, new[] { 2, 2 })]            // no empty extra chunk
    public async Task Build_LineCountVsChunkSize_WritesExpectedChunks(string[] input, int[] expectedLinesPerChunk)
    {
        // Arrange
        // all lines have equal length; a chunk is cut once it reaches the size of two lines
        var twoLinesChunkSize = 2 * (input[0].Length + Environment.NewLine.Length);
        using var temp = new TempDirectory();
        var inputFile = temp.File("input.txt");
        await File.WriteAllLinesAsync(inputFile, input);
        using var workspace = new ChunkWorkspace();
        var sut = new ChunkBuilder(workspace, Workers, twoLinesChunkSize);

        // Act
        var chunks = sut.Build(inputFile, null);

        // Assert
        Assert.Equal(expectedLinesPerChunk, chunks.Paths.Select(c => File.ReadAllLines(c).Length));
        Assert.Equal(input, chunks.Paths.SelectMany(File.ReadAllLines));
        Assert.All(chunks.Paths, path => Assert.StartsWith(workspace.DirectoryPath, path));
    }

    [Fact]
    public async Task Build_AnyDegreeOfParallelism_WritesIdenticalChunks()
    {
        // Arrange
        var random = new Random(7);
        var input = Enumerable.Range(0, 5_000).Select(_ => $"{random.Next(1_000)}. word{random.Next(300)}").ToArray();
        using var temp = new TempDirectory();
        var inputFile = temp.File("input.txt");
        await File.WriteAllLinesAsync(inputFile, input);
        using var singleWorkspace = new ChunkWorkspace();
        using var manyWorkspace = new ChunkWorkspace();

        // Act
        var single = new ChunkBuilder(singleWorkspace, degreeOfParallelism: 1, chunkSize: 2_000).Build(inputFile, null);
        var many = new ChunkBuilder(manyWorkspace, degreeOfParallelism: 8, chunkSize: 2_000).Build(inputFile, null);

        // Assert
        Assert.True(many.Paths.Count > 8, "there should be more chunks than workers");
        Assert.Equal(single.LineCount, many.LineCount);
        Assert.Equal(
            single.Paths.Select(File.ReadAllText),
            many.Paths.Select(File.ReadAllText));
    }

    [Fact]
    public async Task Build_WorkerFails_ThrowsInsteadOfHanging()
    {
        // Arrange
        using var temp = new TempDirectory();
        var inputFile = temp.File("input.txt");
        await File.WriteAllLinesAsync(inputFile, Enumerable.Range(0, 1_000).Select(i => $"{i}. line"));
        var workspace = new ChunkWorkspace();
        workspace.Dispose(); // its directory is gone, so every chunk write fails
        var sut = new ChunkBuilder(workspace, Workers, chunkSize: 1_000);

        // Act
        var build = Task.Run(() => sut.Build(inputFile, null));
        var finished = await Task.WhenAny(build, Task.Delay(TimeSpan.FromSeconds(10)));

        // Assert
        Assert.Same(build, finished);
        await Assert.ThrowsAnyAsync<Exception>(() => build);
    }

    [Fact]
    public void Build_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        using var temp = new TempDirectory();
        using var workspace = new ChunkWorkspace();
        var sut = new ChunkBuilder(workspace, Workers, chunkSize: 1_000);

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => sut.Build(temp.File("missing.txt"), null));
    }
}
