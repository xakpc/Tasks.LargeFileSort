using System.Diagnostics;
using Spectre.Console;
using Xakpc.Tasks.LargeFileSort.Chunking;
using Xakpc.Tasks.LargeFileSort.Merging;

if (args.Length != 2)
{
    AnsiConsole.MarkupLine("Usage: [green]Xakpc.Tasks.LargeFileSort <input-file> <output-file>[/]");
    return 1;
}

var inputFilePath = Path.GetFullPath(args[0]);
var outputFilePath = Path.GetFullPath(args[1]);

if (!File.Exists(inputFilePath))
{
    AnsiConsole.MarkupLineInterpolated($"[red]Input file '{inputFilePath}' does not exist.[/]");
    return 1;
}

if (string.Equals(inputFilePath, outputFilePath, StringComparison.OrdinalIgnoreCase))
{
    AnsiConsole.MarkupLine("[red]Output file must be different from the input file.[/]");
    return 1;
}

var staleDirectories = ChunkWorkspace.DeleteStale();
if (staleDirectories > 0)
{
    AnsiConsole.MarkupLineInterpolated($"Removed {staleDirectories} temp folder(s) left by interrupted runs.");
}

var stopwatch = Stopwatch.StartNew();
SortedChunks chunks = null!;

AnsiConsole.Progress()
    .Columns(
    [
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new ElapsedTimeColumn(),
        new SpinnerColumn()
    ])
    .Start(ctx =>
    {
        var build = ctx.AddTask("Building sorted chunks");
        var merge = ctx.AddTask("Merging chunks", autoStart: false);

        using var workspace = new ChunkWorkspace();
        // benchmarks on 1 GB: no gain beyond 8 workers (reading and the merge are single-threaded), only more memory
        var workers = Math.Min(Environment.ProcessorCount, 8);
        var chunkBuilder = new ChunkBuilder(workspace, workers, chunkSize: 128 * 1024 * 1024);

        chunks = chunkBuilder.Build(inputFilePath, new Progress<double>(value => build.Value = value));

        merge.StartTask();
        ChunkMerger.Merge(chunks, outputFilePath, new Progress<double>(value => merge.Value = value));
    });

AnsiConsole.MarkupLineInterpolated($"Sorted {chunks.LineCount:N0} lines using {chunks.Paths.Count} chunks in {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}.");
AnsiConsole.MarkupLineInterpolated($"Output: {outputFilePath}");

if (chunks.SkippedLines > 0)
{
    AnsiConsole.MarkupLineInterpolated($"[yellow]Skipped {chunks.SkippedLines:N0} malformed lines (expected '<Number>. <String>').[/]");
}

return 0;
