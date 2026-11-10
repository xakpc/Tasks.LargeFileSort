using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Xakpc.Tasks.LargeFileSort.Benchmarks;

/// <summary>
/// Cost of reading a 1 GB file line by line: synchronous vs asynchronous reads, and the default 4 KB buffer
/// vs a 1 MB buffer. Isolates the I/O part of the sorter from parsing and sorting.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5)]
public class ReadBenchmarks
{
    private const int SizeMB = 1024;

    private string _inputFilePath = null!;

    [Params(4 * 1024, 1024 * 1024)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void GlobalSetup() => _inputFilePath = BenchmarkData.EnsureInputFile(SizeMB);

    [Benchmark(Baseline = true)]
    public long ReadLine()
    {
        using var reader = OpenReader(FileOptions.SequentialScan);
        var lines = 0L;
        while (reader.ReadLine() is not null)
        {
            lines++;
        }

        return lines;
    }

    [Benchmark]
    public async Task<long> ReadLineAsync()
    {
        using var reader = OpenReader(FileOptions.SequentialScan);
        var lines = 0L;
        while (await reader.ReadLineAsync() is not null)
        {
            lines++;
        }

        return lines;
    }

    /// <summary>Async reads on a handle opened for overlapped I/O, so refills don't block a thread-pool thread.</summary>
    [Benchmark]
    public async Task<long> ReadLineAsync_AsyncHandle()
    {
        using var reader = OpenReader(FileOptions.SequentialScan | FileOptions.Asynchronous);
        var lines = 0L;
        while (await reader.ReadLineAsync() is not null)
        {
            lines++;
        }

        return lines;
    }

    private StreamReader OpenReader(FileOptions options) =>
        new(new FileStream(_inputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, options),
            bufferSize: BufferSize);
}
