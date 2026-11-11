using Xakpc.Tasks.LargeFileSort.Chunking;
using Xakpc.Tasks.LargeFileSort.Common;

namespace Xakpc.Tasks.LargeFileSort.Merging;

/// <summary>
/// Second phase of the external merge sort: k-way merges sorted chunk files into the output file.
/// </summary>
/// <remarks>
/// Single-threaded: it produces one output stream in global order. Memory is one line per chunk
/// plus one small read buffer per chunk.
/// </remarks>
internal static class ChunkMerger
{
    // the merge keeps one reader per chunk open, so its buffers stay small to bound memory with many chunks
    private const int ChunkReaderBufferSize = 64 * 1024;

    public static void Merge(SortedChunks chunks, string outputFilePath, IProgress<double>? progress)
    {
        // element: the row and the index of the chunk it came from; priority: the row itself
        var queue = new PriorityQueue<(DataRow Row, int ChunkIndex), DataRow>(DataRowComparer.Instance);
        var readers = new List<StreamReader>(chunks.Paths.Count);

        try
        {
            foreach (var path in chunks.Paths)
            {
                readers.Add(BufferedFile.OpenReader(path, ChunkReaderBufferSize));
            }

            for (var i = 0; i < readers.Count; i++)
            {
                EnqueueNextRow(i);
            }

            using var writer = BufferedFile.OpenWriter(outputFilePath);
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

            // chunk files hold only rows that were already parsed successfully
            if (!DataRow.TryParse(line, out var row))
            {
                throw new InvalidDataException($"Chunk file '{chunks.Paths[chunkIndex]}' contains a malformed line.");
            }

            queue.Enqueue((row, chunkIndex), row);
        }
    }
}
