namespace Xakpc.Tasks.LargeFileSort.Chunking;

/// <summary>
/// Private temp directory that holds the chunk files of one sort run.
/// </summary>
/// <remarks>
/// Created as a unique subdirectory of the system temp directory (set the <c>TEMP</c> environment variable to
/// use another drive); <see cref="Dispose"/> deletes it. While the instance is alive it holds an exclusive lock
/// file in the directory, which marks it as in use for <see cref="DeleteStale"/>.
/// </remarks>
internal sealed class ChunkWorkspace : IDisposable
{
    private const string DirectoryPrefix = "LargeFileSort-";
    private const string LockFileName = ".lock";

    private readonly FileStream _lock;

    public ChunkWorkspace()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"{DirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        _lock = AcquireLock(DirectoryPath);
    }

    public string DirectoryPath { get; }

    /// <summary>Path of the chunk file with the given index; chunk files sort by name in index order.</summary>
    public string GetChunkPath(int index) => Path.Combine(DirectoryPath, $"chunk-{index:D5}.txt");

    /// <summary>
    /// Deletes workspaces left in the temp directory by runs that were killed before <see cref="Dispose"/>
    /// could clean up. Workspaces of runs that are still alive, in this or another process, are skipped.
    /// </summary>
    /// <returns>Number of deleted directories.</returns>
    public static int DeleteStale()
    {
        var deleted = 0;
        foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), $"{DirectoryPrefix}*"))
        {
            try
            {
                // succeeds only if no live workspace holds the lock
                AcquireLock(directory).Dispose();
                Directory.Delete(directory, recursive: true);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use by a live workspace (or already gone) - leave it
            }
        }

        return deleted;
    }

    public void Dispose()
    {
        _lock.Dispose();

        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private static FileStream AcquireLock(string directory) =>
        new(Path.Combine(directory, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
}
