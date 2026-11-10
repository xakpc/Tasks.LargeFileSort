using System.Diagnostics;
using Spectre.Console;
using Xakpc.Tasks.LargeFileSort.Services;

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

var stopwatch = Stopwatch.StartNew();
SortedChunks chunks = null!;

await AnsiConsole.Progress()
    .Columns(
    [
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new ElapsedTimeColumn(),
        new SpinnerColumn()
    ])
    .StartAsync(async ctx =>
    {
        var build = ctx.AddTask("Building sorted chunks");
        var merge = ctx.AddTask("Merging chunks", autoStart: false);

        using var sortingService = new SortingService();

        chunks = await sortingService.BuildSortedChunksAsync(
            inputFilePath, new Progress<double>(value => build.Value = value));

        merge.StartTask();
        await sortingService.MergeChunksAsync(
            chunks, outputFilePath, new Progress<double>(value => merge.Value = value));
    });

AnsiConsole.MarkupLineInterpolated($"Sorted {chunks.LineCount:N0} lines using {chunks.Paths.Count} chunks in {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}.");
AnsiConsole.MarkupLineInterpolated($"Output: {outputFilePath}");

if (chunks.SkippedLines > 0)
{
    AnsiConsole.MarkupLineInterpolated($"[yellow]Skipped {chunks.SkippedLines:N0} malformed lines (expected '<Number>. <String>').[/]");
}

return 0;
