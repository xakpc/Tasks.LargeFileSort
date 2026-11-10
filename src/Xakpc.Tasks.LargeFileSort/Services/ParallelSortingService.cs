using System.Diagnostics;

namespace Xakpc.Tasks.LargeFileSort.Services;

/// <summary>
/// External merge sort: split the input into sorted chunk files that fit in memory, then k-way merge them.
/// </summary>
/// <remarks>
/// Chunk files are written to a unique subdirectory of the system temp directory (set the <c>TEMP</c>
/// environment variable to use another drive); <see cref="Dispose"/> deletes only that subdirectory.
/// While the instance is alive it holds an exclusive lock file in that subdirectory, which marks it as in use
/// for <see cref="DeleteStaleWorkingDirectories"/>.
/// </remarks>
internal sealed class ParallelSortingService : IDisposable
{
    private const string WorkingDirectoryPrefix = "LargeFileSort-";
    private const string LockFileName = ".lock";
    private readonly int _degreeOfParallelism;
    private readonly int _chunkSize;
    private readonly FileStream _lock;

    public string WorkingDirectory { get; }

    /// <param name="chunkSize">Chunk size to for merge.</param>
    public ParallelSortingService(int degreeOfParallelism, int chunkSize = 512 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        _degreeOfParallelism = degreeOfParallelism;
        _chunkSize = chunkSize;
        WorkingDirectory = Path.Combine(Path.GetTempPath(), $"{WorkingDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkingDirectory);
        _lock = AcquireLock(WorkingDirectory);
    }

    /// <summary>
    /// Deletes working directories left in the temp directory by runs that were killed before
    /// <see cref="Dispose"/> could clean up. Directories of instances that are still running are skipped.
    /// </summary>
    /// <returns>Number of deleted directories.</returns>
    public static int DeleteStaleWorkingDirectories()
    {
        var deleted = 0;
        foreach (var directory in Directory.EnumerateDirectories(Path.GetTempPath(), $"{WorkingDirectoryPrefix}*"))
        {
            try
            {
                // succeeds only if no running instance holds the lock
                AcquireLock(directory).Dispose();
                Directory.Delete(directory, recursive: true);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use by a running instance (or already gone) - leave it
            }
        }

        return deleted;
    }

    private static FileStream AcquireLock(string directory) =>
        new(Path.Combine(directory, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>
    /// Reads the input file and writes it as a set of sorted chunk files.
    /// Lines that do not match the expected format are skipped and counted.
    /// </summary>
    public async Task<SortedChunks> BuildSortedChunksAsync(string inputFilePath, IProgress<double>? progress)
    {
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException($"The file '{inputFilePath}' does not exist.", inputFilePath);
        }

        var totalChars = Math.Max(1L, new FileInfo(inputFilePath).Length);
        var rows = new List<DataRow>();
        var chunkPaths = new List<string>();
        var processedChars = 0L;
        var currentChunkSize = 0L;
        var lineCount = 0L;
        var skippedLines = 0L;

        await foreach (var line in File.ReadLinesAsync(inputFilePath))
        {
            // the size accounting is only used for chunking and progress, so an approximate newline length is fine
            currentChunkSize += line.Length + Environment.NewLine.Length;

            if (!DataRow.TryParse(line, out var row))
            {
                skippedLines++;
                continue;
            }

            rows.Add(row);

            if (currentChunkSize >= _chunkSize)
            {
                WriteChunk();
            }
        }

        if (rows.Count > 0)
        {
            WriteChunk();
        }

        progress?.Report(100);
        return new SortedChunks(chunkPaths, lineCount, skippedLines);

        void WriteChunk()
        {
            rows.Sort(DataRowComparer.Instance);

            var chunkPath = Path.Combine(WorkingDirectory, $"chunk-{chunkPaths.Count:D5}.txt");
            File.WriteAllLines(chunkPath, rows.Select(r => r.Line));
            chunkPaths.Add(chunkPath);
            lineCount += rows.Count;
            rows = [];

            processedChars += currentChunkSize;
            currentChunkSize = 0;
            progress?.Report(Math.Min(100.0, processedChars * 100.0 / totalChars));
        }
    }

    /// <summary>
    /// K-way merges sorted chunk files into the output file.
    /// </summary>
    public async Task MergeChunksAsync(SortedChunks chunks, string outputFilePath, IProgress<double>? progress)
    {
        // element: the row and the index of the chunk it came from; priority: the row itself
        var queue = new PriorityQueue<(DataRow Row, int ChunkIndex), DataRow>(DataRowComparer.Instance);
        var readers = new List<StreamReader>(chunks.Paths.Count);

        try
        {
            foreach (var path in chunks.Paths)
            {
                readers.Add(new StreamReader(path));
            }

            for (var i = 0; i < readers.Count; i++)
            {
                await EnqueueNextRowAsync(i);
            }

            await using var writer = new StreamWriter(outputFilePath, append: false);
            var mergedLines = 0L;
            var reportEvery = Math.Max(1L, chunks.LineCount / 100);

            while (queue.TryDequeue(out var item, out _))
            {
                await writer.WriteLineAsync(item.Row.Line);
                await EnqueueNextRowAsync(item.ChunkIndex);

                if (++mergedLines % reportEvery == 0)
                {
                    progress?.Report(Math.Min(100.0, mergedLines * 100.0 / chunks.LineCount));
                }
            }

            progress?.Report(100);
        }
        finally
        {
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }

        async Task EnqueueNextRowAsync(int chunkIndex)
        {
            var line = await readers[chunkIndex].ReadLineAsync();
            if (line is null)
            {
                return;
            }

            // chunk files are written by this class from already validated rows
            if (!DataRow.TryParse(line, out var row))
            {
                throw new InvalidDataException($"Chunk file '{chunks.Paths[chunkIndex]}' contains a malformed line.");
            }

            queue.Enqueue((row, chunkIndex), row);
        }
    }

    public void Dispose()
    {
        _lock.Dispose();

        if (Directory.Exists(WorkingDirectory))
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
    }
}
