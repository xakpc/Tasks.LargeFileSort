using Spectre.Console;

namespace Xakpc.Tasks.LargeFileSort.Services;

internal interface ISortingService
{
    /// <summary>
    /// Create multiple sorted temporary files (chunks)
    /// </summary>
    Task<IList<string> > BuildSortedChunksAsync(string inputFilePath, IProgress<double> progress);

    /// <summary>
    /// Merge sorted chunks together to a final file
    /// </summary>
    /// <param name="chunks"></param>
    /// <param name="outputFilePath"></param>
    Task MergeChunksAsync(IList<string> chunks, string outputFilePath);
}

public class SortingService : ISortingService, IDisposable
{
    private readonly string _tempFilesDirectory;
    private readonly DataRowComparer _comparer;
    private const int ChunkSize = 512*1024*1024; // 512 MB

    
    public SortingService(string tempFilesDirectory)
    {
        if (!Directory.Exists(tempFilesDirectory))
        {
            Directory.CreateDirectory(tempFilesDirectory);
        }

        _tempFilesDirectory = tempFilesDirectory;
        _comparer = new DataRowComparer();
    }
    
    /// <inheritdoc/>
    public async Task<IList<string>> BuildSortedChunksAsync(string filePath, IProgress<double>? progress)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The file '{filePath}' does not exist.");
        }

        var list = new List<DataRow>();
        var sortedChunks = new List<string>();

        var totalBytes = GetFileSize(filePath);
        var processedBytes = 0L;

        int currentChunkSize = 0;
        var streamReader = new StreamReader(filePath);
        await foreach (var str in File.ReadLinesAsync(filePath))
        {
            var row = ParseLine(str);
            if (row == null)
            {
                continue; // skip bad rows
            }

            list.Add(row.Value);

            currentChunkSize += str.Length + Environment.NewLine.Length;

            if (currentChunkSize >= ChunkSize)
            {
                ProcessChunk();
                list = [];
            }
        }

        // process leftovers
        if (list.Count > 0)
        {
            ProcessChunk();
        }

        return sortedChunks;

        void ProcessChunk()
        {
            list.Sort(_comparer);

            var tempPath = Path.Combine(_tempFilesDirectory, Path.GetRandomFileName());
            File.WriteAllLines(tempPath, list.Select(x => $"{x.Number}. {x.Text}"));
            sortedChunks.Add(tempPath);

            processedBytes += currentChunkSize;
            progress?.Report(processedBytes * 100.0 / totalBytes);

            currentChunkSize = 0;
        }
    }

    /// <inheritdoc/>
    public async Task MergeChunksAsync(IList<string> chunks, string outputFilePath)
    {
        // we store the data in priority quest, with index of chunk where it comes from
        var priorityQueue = new PriorityQueue<(DataRow Row, int ChunkIndex), DataRow>(_comparer);

        var chunkReaders = new List<StreamReader>(chunks.Count);

        try
        {
            foreach (var chunk in chunks)
            {
                var reader = new StreamReader(chunk);
                chunkReaders.Add(reader);
            }

            // Initialize the priority queue with the first line from each chunk
            for (int i = 0; i < chunkReaders.Count; i++)
            {
                var line = await chunkReaders[i].ReadLineAsync();
                if (line != null)
                {
                    var row = ParseLine(line)!.Value; // should not have bad rows anymore
                    priorityQueue.Enqueue((row, i), row); // sort by text first, then number!
                }
            }

            using var writer = new StreamWriter(outputFilePath, append: false);

            // K-way merge: repeatedly extract the smallest element and add the next element from the same chunk
            while (priorityQueue.Count > 0)
            {
                var (Row, ChunkIndex) = priorityQueue.Dequeue();

                await writer.WriteLineAsync($"{Row.Number}. {Row.Text}");

                var nextLine = await chunkReaders[ChunkIndex].ReadLineAsync();

                if (nextLine == null)
                {
                    continue;
                }

                var nextRow = ParseLine(nextLine)!.Value; // should not have bad rows anymore
                priorityQueue.Enqueue((nextRow, ChunkIndex), nextRow);
            }
        }
        finally
        {
            foreach (var reader in chunkReaders)
            {
                reader.Dispose();
            }
        }
    }

    private long GetFileSize(string filePath)
    {
        long fileSize = 0;
        if (File.Exists(filePath))
        {
            FileInfo info = new FileInfo(filePath);
            fileSize = info.Length;
        }

        return fileSize;
    }

    private static DataRow? ParseLine(string line)
    {
        var parts = line.Split(". ");

        if (parts.Length < 2)
        {
            return null; // reject bad rows
        }

        var number = ulong.Parse(parts[0]);
        var text = parts[1].Trim();
        return new DataRow(number, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFilesDirectory))
        {
            Directory.Delete(_tempFilesDirectory, recursive: true);
        }
    }


}