namespace Xakpc.Tasks.LargeFileSort.Tests;

/// <summary>
/// Unique temp directory for a single test, deleted on dispose.
/// Used explicitly via <c>using var</c> in each test instead of hidden setup/teardown.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "LargeFileSortTests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
