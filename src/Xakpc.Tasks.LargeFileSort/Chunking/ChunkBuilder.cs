using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Xakpc.Tasks.LargeFileSort.Common;

namespace Xakpc.Tasks.LargeFileSort.Chunking;

/// <summary>
/// First phase of the external merge sort: splits the input into sorted chunk files. One sequential reader
/// cuts the input into chunks, and up to N workers parse, sort and write them at the same time.
/// </summary>
/// <remarks>
/// <para>
/// Live data is about <c>(degreeOfParallelism + 1) × chunkSize × 3.5</c> bytes: each worker holds one chunk
/// and the reader fills one more. Strings are UTF-16 and every line also costs a list slot and a
/// <see cref="DataRow"/>, hence the factor. The measured peak is about twice that (7.5 GB with 8 workers and
/// 128 MB chunks), because finished chunks stay on the heap until the next full garbage collection.
/// </para>
/// <para>
/// Chunk files are named by chunk index, not by completion order, so the result is the same for any
/// degree of parallelism. Chunk files are written to the given <see cref="ChunkWorkspace"/>, which the caller
/// owns and disposes.
/// </para>
/// </remarks>
internal sealed class ChunkBuilder
{
    private readonly ChunkWorkspace _workspace;
    private readonly int _degreeOfParallelism;
    private readonly int _chunkSize;

    /// <param name="workspace">Where chunk files are written.</param>
    /// <param name="degreeOfParallelism">Maximum number of chunks parsed, sorted and written at the same time.</param>
    /// <param name="chunkSize">Approximate amount of input text (in characters) per chunk.</param>
    public ChunkBuilder(ChunkWorkspace workspace, int degreeOfParallelism, int chunkSize)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degreeOfParallelism);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);

        _workspace = workspace;
        _degreeOfParallelism = degreeOfParallelism;
        _chunkSize = chunkSize;
    }

    /// <summary>
    /// Reads the input file and writes it as a set of sorted chunk files.
    /// Lines that do not match the expected format are skipped and counted.
    /// </summary>
    public SortedChunks Build(string inputFilePath, IProgress<double>? progress)
    {
        // checked up front so a missing file is a plain FileNotFoundException, not one wrapped by Parallel.ForEach
        if (!File.Exists(inputFilePath))
        {
            throw new FileNotFoundException($"The file '{inputFilePath}' does not exist.", inputFilePath);
        }

        // workers finish in any order, so results go to a thread-safe bag and the counter uses Interlocked
        var written = new ConcurrentBag<(int Index, string Path, int LineCount)>();
        var skippedLines = 0L;

        // Parallel.ForEach pulls items from an IEnumerable through a partitioner. By default it grabs items in
        // growing batches, which would let one worker hold several 128 MB chunks at once. NoBuffering makes every
        // worker take exactly one chunk, and only when it is free, which bounds memory to N + 1 chunks.
        // The partitioner calls MoveNext under a lock, so ReadChunks runs on one thread at a time, in file order.
        var chunks = Partitioner.Create(ReadChunks(inputFilePath, progress), EnumerablePartitionerOptions.NoBuffering);
        var options = new ParallelOptions { MaxDegreeOfParallelism = _degreeOfParallelism };

        try
        {
            Parallel.ForEach(chunks, options, chunk =>
            {
                var (rows, skipped) = Parse(chunk.Lines);
                Interlocked.Add(ref skippedLines, skipped);

                rows.Sort(DataRowComparer.Instance);

                var path = _workspace.GetChunkPath(chunk.Index);
                using (var writer = BufferedFile.OpenWriter(path))
                {
                    foreach (var row in rows)
                    {
                        writer.WriteLine(row.Line);
                    }
                }

                written.Add((chunk.Index, path, rows.Count));
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            // Parallel.ForEach wraps worker failures; with a single failure, surface the original exception
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }

        progress?.Report(100);

        var ordered = written.OrderBy(c => c.Index).ToList();
        return new SortedChunks(
            ordered.Select(c => c.Path).ToList(),
            ordered.Sum(c => (long)c.LineCount),
            skippedLines);
    }

    /// <summary>
    /// Lazily cuts the input into chunks of about <c>chunkSize</c> characters, without parsing.
    /// </summary>
    private IEnumerable<Chunk> ReadChunks(string inputFilePath, IProgress<double>? progress)
    {
        using var reader = BufferedFile.OpenReader(inputFilePath);
        var totalBytes = Math.Max(1L, reader.BaseStream.Length);
        var lines = new List<string>();
        var chunkChars = 0L;
        var index = 0;

        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);

            // only used to cut chunks, so an approximate newline length is fine
            chunkChars += line.Length + Environment.NewLine.Length;

            if (chunkChars >= _chunkSize)
            {
                yield return new Chunk(index++, lines);
                lines = [];
                chunkChars = 0;
                progress?.Report(Math.Min(100.0, reader.BaseStream.Position * 100.0 / totalBytes));
            }
        }

        if (lines.Count > 0)
        {
            yield return new Chunk(index, lines);
        }
    }

    /// <summary>Parses the lines of one chunk; malformed lines are skipped and counted.</summary>
    private static (List<DataRow> Rows, int Skipped) Parse(List<string> lines)
    {
        var rows = new List<DataRow>(lines.Count);
        var skipped = 0;

        foreach (var line in lines)
        {
            if (DataRow.TryParse(line, out var row))
            {
                rows.Add(row);
            }
            else
            {
                skipped++;
            }
        }

        return (rows, skipped);
    }

    /// <summary>A run of consecutive input lines; <paramref name="Index"/> is its position in the input.</summary>
    private sealed record Chunk(int Index, List<string> Lines);
}
