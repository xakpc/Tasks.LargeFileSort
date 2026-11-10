namespace Xakpc.Tasks.LargeFileSort.Services;

/// <summary>Result of splitting the input into sorted chunk files.</summary>
/// <param name="Paths">Sorted chunk files, in creation order.</param>
/// <param name="LineCount">Number of valid lines written to the chunks.</param>
/// <param name="SkippedLines">Number of input lines that did not match the expected format.</param>
internal sealed record SortedChunks(IReadOnlyList<string> Paths, long LineCount, long SkippedLines);

/// <summary>
/// External merge sort: split the input into sorted chunk files that fit in memory, then k-way merge them.
/// </summary>
/// <remarks>
/// Chunk files are written to a unique subdirectory of the system temp directory (set the <c>TEMP</c>
/// environment variable to use another drive); <see cref="Dispose"/> deletes only that subdirectory.
/// </remarks>
internal sealed class SortingService : IDisposable
{
    private readonly int _chunkSize;

    public string WorkingDirectory { get; }

    /// <param name="chunkSize">Chunk size to for merge.</param>
    public SortingService(int chunkSize = 512 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        _chunkSize = chunkSize;
        WorkingDirectory = Path.Combine(Path.GetTempPath(), $"LargeFileSort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(WorkingDirectory);
    }

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
        if (Directory.Exists(WorkingDirectory))
        {
            Directory.Delete(WorkingDirectory, recursive: true);
        }
    }
}
