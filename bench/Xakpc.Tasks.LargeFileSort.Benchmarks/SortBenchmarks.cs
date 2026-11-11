using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Xakpc.Tasks.LargeFileSort.Chunking;
using Xakpc.Tasks.LargeFileSort.Merging;

namespace Xakpc.Tasks.LargeFileSort.Benchmarks;

/// <summary>
/// End-to-end sort of a generated file with different numbers of workers.
/// Each operation takes seconds, so a few monitored iterations are enough.
/// </summary>
[MemoryDiagnoser]
[GcServer(true)] // same GC mode as the sorter app
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class SortBenchmarks
{
    private const int BytesPerMegabyte = 1024 * 1024;

    private string _inputFilePath = null!;
    private string _outputFilePath = null!;

    [Params(1024)]
    public int SizeMB { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _inputFilePath = BenchmarkData.EnsureInputFile(SizeMB);
        _outputFilePath = Path.Combine(BenchmarkData.Directory, $"output-{SizeMB}MB.txt");
    }

    [IterationCleanup]
    public void IterationCleanup() => File.Delete(_outputFilePath);

    /// <summary>Chunks must outnumber workers, otherwise some workers have nothing to sort.</summary>
    [Benchmark]
    [Arguments(1, 128)]
    [Arguments(2, 128)]
    [Arguments(4, 128)]
    [Arguments(8, 128)]
    [Arguments(16, 64)]
    public void Sort(int workers, int chunkSizeMB)
    {
        using var workspace = new ChunkWorkspace();
        var chunks = new ChunkBuilder(workspace, workers, chunkSizeMB * BytesPerMegabyte).Build(_inputFilePath, null);
        ChunkMerger.Merge(chunks, _outputFilePath, null);
    }
}
