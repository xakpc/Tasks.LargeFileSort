using Spectre.Console;
using Xakpc.Tasks.LargeFileSort.Generator.Services;

const string wordsDictionary = "google-10000-english-no-swears.txt";
const string filePath = "..\\..\\..\\..\\..\\build\\input-100GB.txt";

// read words dict
var words = await File.ReadAllLinesAsync(wordsDictionary);


var finalFileSizeMB = AnsiConsole.Ask<ulong>("Enter desired file size in [green]decimal MB[/]:");

if (finalFileSizeMB <= 0)
{
    AnsiConsole.Write(new Text("File size must be greater than 0.", 
        new Style(foreground: Color.Red)));
    return;
}

ulong fileSizeBytes = finalFileSizeMB * 1024 * 1024;

var spinner = AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots);

var result = AnsiConsole.Progress()
    .Columns(
    [
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn(),
        new SpinnerColumn()
    ])
    .Start(ctx =>
    {
        var generate = ctx.AddTask("Generating", maxValue: fileSizeBytes);
        var progress = new Progress<double>(value => generate.Value = value);
        var generator = new Generator(words, filePath, fileSizeBytes);
        return generator.Generate(progress);
    });

var multiLine = new Text($"File created at {filePath}\nSize: {result.Size/(1024*1024)} MB\nNumber of lines: {result.Lines}\nNumber of duplicates: {result.Dupes}");
AnsiConsole.Write(multiLine);
AnsiConsole.Write(Text.NewLine);
