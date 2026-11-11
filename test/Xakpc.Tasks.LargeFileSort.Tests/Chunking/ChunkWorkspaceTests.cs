using Xakpc.Tasks.LargeFileSort.Chunking;

namespace Xakpc.Tasks.LargeFileSort.Tests.Chunking;

[Trait("Category", "Integration")]
public class ChunkWorkspaceTests
{
    [Fact]
    public void Constructor_CreatesUniqueDirectoryUnderSystemTemp()
    {
        // Act
        using var first = new ChunkWorkspace();
        using var second = new ChunkWorkspace();

        // Assert
        Assert.StartsWith(Path.GetTempPath(), first.DirectoryPath);
        Assert.True(Directory.Exists(first.DirectoryPath));
        Assert.NotEqual(first.DirectoryPath, second.DirectoryPath);
    }

    [Fact]
    public void GetChunkPath_ReturnsPathsInsideWorkspaceThatSortByIndex()
    {
        // Arrange
        using var sut = new ChunkWorkspace();

        // Act
        var paths = new[] { 10, 2, 1 }.Select(sut.GetChunkPath).ToArray();

        // Assert
        Assert.All(paths, path => Assert.Equal(sut.DirectoryPath, Path.GetDirectoryName(path)));
        Assert.Equal([paths[2], paths[1], paths[0]], paths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Dispose_DeletesDirectoryWithChunks()
    {
        // Arrange
        var sut = new ChunkWorkspace();
        var chunkPath = sut.GetChunkPath(0);
        File.WriteAllText(chunkPath, "1. a");

        // Act
        sut.Dispose();

        // Assert
        Assert.False(Directory.Exists(sut.DirectoryPath));
    }

    [Fact]
    public void DeleteStale_DeletesAbandonedAndKeepsLive()
    {
        // Arrange
        var abandoned = Path.Combine(Path.GetTempPath(), $"LargeFileSort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(abandoned);
        File.WriteAllText(Path.Combine(abandoned, "chunk-00000.txt"), "1. a");
        using var live = new ChunkWorkspace();

        // Act
        var deleted = ChunkWorkspace.DeleteStale();

        // Assert
        Assert.True(deleted >= 1);
        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(live.DirectoryPath));
    }
}
