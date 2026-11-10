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
internal sealed class SortingService : IDisposable
{
    private const string WorkingDirectoryPrefix = "LargeFileSort-";
    private const string LockFileName = ".lock";

    // large buffers for the single sequential input/output streams; see ReadBenchmarks
    private const int StreamBufferSize = 1024 * 1024;

    // the merge keeps one reader per chunk open, so its buffers stay small to bound memory with many chunks
    private const int ChunkReaderBufferSize = 64 * 1024;

    private readonly int _chunkSize;
    private readonly FileStream _lock;

    public string WorkingDirectory { get; }

    /// <param name="chunkSize">Chunk size to for merge.</param>
    public SortingService(int chunkSize = 512 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

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
    public SortedChunks BuildSortedChunks(string inputFilePath, IProgress<double>? progress)
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

        using var reader = OpenReader(inputFilePath, StreamBufferSize);

        while (reader.ReadLine() is { } line)
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
            using (var writer = OpenWriter(chunkPath))
            {
                foreach (var sortedRow in rows)
                {
                    writer.WriteLine(sortedRow.Line);
                }
            }

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
    public void MergeChunks(SortedChunks chunks, string outputFilePath, IProgress<double>? progress)
    {
        // element: the row and the index of the chunk it came from; priority: the row itself
        var queue = new PriorityQueue<(DataRow Row, int ChunkIndex), DataRow>(DataRowComparer.Instance);
        var readers = new List<StreamReader>(chunks.Paths.Count);

        try
        {
            foreach (var path in chunks.Paths)
            {
                readers.Add(OpenReader(path, ChunkReaderBufferSize));
            }

            for (var i = 0; i < readers.Count; i++)
            {
                EnqueueNextRow(i);
            }

            using var writer = OpenWriter(outputFilePath);
            var mergedLines = 0L;
            var reportEvery = Math.Max(1L, chunks.LineCount / 100);

            while (queue.TryDequeue(out var item, out _))
            {
                writer.WriteLine(item.Row.Line);
                EnqueueNextRow(item.ChunkIndex);

                if (++mergedLines % reportEvery == 0)
                {
                    progress?.Report(Math.Min(100.0, mergedLines * 100.0 / chunks.LineCount));
                }
            }

            progress?.Report(100);
        }
        finally
        {
            // don't forget to close all readers
            foreach (var reader in readers)
            {
                reader.Dispose();
            }
        }

        void EnqueueNextRow(int chunkIndex)
        {
            var line = readers[chunkIndex].ReadLine();
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

    private static StreamReader OpenReader(string path, int bufferSize) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan),
            bufferSize: bufferSize);

    private static StreamWriter OpenWriter(string path) =>
        new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize),
            bufferSize: StreamBufferSize);

    public void Dispose()
    {
        _lock.Dispose();

        if (Directory.Exists(WorkingDirectory))
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
    }
}
