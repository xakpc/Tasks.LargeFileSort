using Spectre.Console;
using Xakpc.Tasks.LargeFileSort.Services;

const string filePath = "..\\..\\..\\..\\..\\build\\input-10GB.txt";
const string outputFilePath = "..\\..\\..\\..\\..\\build\\output_sorted.txt";
var tempFilesDirectory = Path.Combine(Path.GetTempPath(), "LargeFileSortTemp");

// Ensure the temporary files directory exists
Directory.CreateDirectory(tempFilesDirectory);

await AnsiConsole.Progress()
    .Columns(
    [
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn(),
        new SpinnerColumn()
    ])
    .StartAsync(async ctx =>
    {
        var build = ctx.AddTask("Bulding Sorted Chunks");
        var merge = ctx.AddTask("Merging Chunks", autoStart: false);

        using var sortingService = new SortingService(tempFilesDirectory);

        var buildProgress = new Progress<double>(value => build.Value = value);
        // read original file into sorted chunks
        var chunks = await sortingService.BuildSortedChunksAsync(filePath, buildProgress);
        AnsiConsole.WriteLine($"Created {chunks.Count} temporary files for sorting as {tempFilesDirectory}.");

        merge.StartTask();
        // Merge the sorted chunks into a single output file
        await sortingService.MergeChunksAsync(chunks, outputFilePath);

        AnsiConsole.WriteLine($"Sorted file created at {outputFilePath}.");
    });


