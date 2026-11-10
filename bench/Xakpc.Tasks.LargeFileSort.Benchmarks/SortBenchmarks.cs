using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Xakpc.Tasks.LargeFileSort.Services;

namespace Xakpc.Tasks.LargeFileSort.Benchmarks;

/// <summary>
/// End-to-end sort of a generated file. Each operation takes seconds, so a few monitored iterations are enough.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class SortBenchmarks
{
    private string _inputFilePath = null!;
    private string _outputFilePath = null!;

    [Params(1024, 5120)]
    public int SizeMB { get; set; }

    /// <summary>Chunk size in millions of characters (~MB for ASCII input).</summary>
    [Params(64, 256, 512, 1024)]
    public int ChunkSizeMB { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _inputFilePath = BenchmarkData.EnsureInputFile(SizeMB);
        _outputFilePath = Path.Combine(BenchmarkData.Directory, $"output-{SizeMB}MB.txt");
    }

    [IterationCleanup]
    public void IterationCleanup() => File.Delete(_outputFilePath);

    [Benchmark]
    public void Sort()
    {
        using var sorter = new SortingService(ChunkSizeMB * 1024 * 1024);
        var chunks = sorter.BuildSortedChunks(_inputFilePath, null);
        sorter.MergeChunks(chunks, _outputFilePath, null);
    }
}
