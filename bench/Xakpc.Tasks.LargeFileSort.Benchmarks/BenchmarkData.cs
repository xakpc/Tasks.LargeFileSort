using Xakpc.Tasks.LargeFileSort.Generator.Generation;

namespace Xakpc.Tasks.LargeFileSort.Benchmarks;

/// <summary>Generated input files shared by all benchmarks, cached in the temp directory between runs.</summary>
internal static class BenchmarkData
{
    private const long BytesPerMegabyte = 1024 * 1024;
    private const string WordsDictionary = "google-10000-english-no-swears.txt";

    public static string Directory { get; } = Path.Combine(Path.GetTempPath(), "LargeFileSortBenchmarks");

    /// <summary>Returns the path of a generated input file of the given size, generating it if missing.</summary>
    public static string EnsureInputFile(int sizeMB)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"input-{sizeMB}MB.txt");

        var sizeBytes = sizeMB * BytesPerMegabyte;
        if (!File.Exists(path) || new FileInfo(path).Length < sizeBytes)
        {
            var words = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, WordsDictionary));
            new PhraseGenerator(words, path, sizeBytes).Generate(null);
        }

        return path;
    }
}
