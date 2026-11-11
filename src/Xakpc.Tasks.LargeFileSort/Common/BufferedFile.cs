namespace Xakpc.Tasks.LargeFileSort.Common;

/// <summary>
/// Opens text files for sequential line-by-line I/O with explicit buffer sizes.
/// </summary>
/// <remarks>
/// Synchronous reads with a large buffer are the fastest way to read lines; see ReadBenchmarks.
/// </remarks>
internal static class BufferedFile
{
    /// <summary>Buffer for a single sequential input or output stream.</summary>
    public const int LargeBufferSize = 1024 * 1024;

    public static StreamReader OpenReader(string path, int bufferSize = LargeBufferSize) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan),
            bufferSize: bufferSize);

    public static StreamWriter OpenWriter(string path, int bufferSize = LargeBufferSize) =>
        new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize),
            bufferSize: bufferSize);
}
