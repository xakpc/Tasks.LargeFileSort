namespace Xakpc.Tasks.LargeFileSort.Chunking;

/// <summary>Result of splitting the input into sorted chunk files.</summary>
/// <param name="Paths">Sorted chunk files, in creation order.</param>
/// <param name="LineCount">Number of valid lines written to the chunks.</param>
/// <param name="SkippedLines">Number of input lines that did not match the expected format.</param>
internal sealed record SortedChunks(IReadOnlyList<string> Paths, long LineCount, long SkippedLines);
