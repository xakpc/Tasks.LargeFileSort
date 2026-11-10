using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Xakpc.Tasks.LargeFileSort.Generator.Services;
using Xakpc.Tasks.LargeFileSort.Services;

namespace Xakpc.Tasks.LargeFileSort.Benchmarks;

/// <summary>
/// End-to-end sort of a generated file. Each operation takes seconds, so a few monitored iterations are
/// enough; the input file is generated once and cached in the temp directory between runs.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class SortBenchmarks
{
    private const long BytesPerMegabyte = 1024 * 1024;
    private const string WordsDictionary = "google-10000-english-no-swears.txt";

    private static readonly string DataDirectory = Path.Combine(Path.GetTempPath(), "LargeFileSortBenchmarks");

    private string _inputFilePath = null!;
    private string _outputFilePath = null!;

    [Params(1024)]
    public int SizeMB { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        Directory.CreateDirectory(DataDirectory);
        _inputFilePath = Path.Combine(DataDirectory, $"input-{SizeMB}MB.txt");
        _outputFilePath = Path.Combine(DataDirectory, $"output-{SizeMB}MB.txt");

        var sizeBytes = SizeMB * BytesPerMegabyte;
        if (!File.Exists(_inputFilePath) || new FileInfo(_inputFilePath).Length < sizeBytes)
        {
            var words = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, WordsDictionary));
            new PhraseGenerator(words, _inputFilePath, sizeBytes).Generate(null);
        }
    }

    [IterationCleanup]
    public void IterationCleanup() => File.Delete(_outputFilePath);

    [Benchmark(Baseline = true, Description = "Sequential")]
    public async Task SequentialSort()
    {
        using var sorter = new SortingService();
        var chunks = await sorter.BuildSortedChunksAsync(_inputFilePath, null);
        await sorter.MergeChunksAsync(chunks, _outputFilePath, null);
    }
}
