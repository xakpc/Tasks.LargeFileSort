using System.Diagnostics;
using Spectre.Console;
using Xakpc.Tasks.LargeFileSort.Generator.Services;

const long BytesPerMegabyte = 1024 * 1024;
const string WordsDictionary = "google-10000-english-no-swears.txt";

// generated files go to the repository's build folder: build/bin/<project>/<configuration>/<tfm>/ -> build/
var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

var sizeMegabytes = AnsiConsole.Prompt(
    new TextPrompt<long>("Enter file size in [green]MB[/] (1 MB = 1,048,576 bytes):")
        .Validate(size => size > 0, "Size must be greater than 0."));

var outputFilePath = Path.Combine(outputDirectory, $"input-{sizeMegabytes}MB.txt");
var words = await File.ReadAllLinesAsync(Path.Combine(AppContext.BaseDirectory, WordsDictionary));
var fileSizeBytes = sizeMegabytes * BytesPerMegabyte;
var stopwatch = Stopwatch.StartNew();

var result = AnsiConsole.Progress()
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
        var task = ctx.AddTask("Generating", maxValue: fileSizeBytes);
        var generator = new PhraseGenerator(words, outputFilePath, fileSizeBytes);
        return generator.Generate(new Progress<double>(value => task.Value = value));
    });

AnsiConsole.MarkupLineInterpolated($"File created at {outputFilePath} in {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
AnsiConsole.MarkupLineInterpolated($"Size: {result.SizeBytes / BytesPerMegabyte:N0} MB ({result.SizeBytes:N0} bytes)");
AnsiConsole.MarkupLineInterpolated($"Lines: {result.Lines:N0}, of which {result.RepeatedLines:N0} reuse an earlier String part");
