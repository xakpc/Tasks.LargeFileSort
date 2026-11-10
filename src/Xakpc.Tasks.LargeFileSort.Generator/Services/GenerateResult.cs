namespace Xakpc.Tasks.LargeFileSort.Generator.Services;

/// <param name="Lines">Number of lines written.</param>
/// <param name="SizeBytes">Size of the written file in bytes.</param>
/// <param name="RepeatedLines">Number of lines whose String part was deliberately reused from an earlier line.</param>
internal sealed record GenerateResult(long Lines, long SizeBytes, long RepeatedLines);
