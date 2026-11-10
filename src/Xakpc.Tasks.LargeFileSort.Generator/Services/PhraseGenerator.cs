using System.Text;

namespace Xakpc.Tasks.LargeFileSort.Generator.Services;

/// <summary>
/// Writes a test file of lines in the format <c>&lt;Number&gt;. &lt;String&gt;</c> where the String part is
/// a phrase of 1-3 random words. About 10% of the lines reuse an earlier phrase so the file always contains
/// repeated String parts. Output is deterministic for the same inputs.
/// </summary>
internal sealed class PhraseGenerator
{
    private const int Seed = 46;
    private const double RepeatProbability = 0.1;
    private const int RepeatPoolCapacity = 10_000;

    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly int NewLineBytes = Utf8.GetByteCount(Environment.NewLine);

    private readonly string _filePath;
    private readonly string[] _words;
    private readonly long _fileSizeBytes;
    private readonly Random _random = new(Seed);

    /// <param name="words">Dictionary to build phrases from; blank entries are ignored.</param>
    /// <param name="filePath">Output file; overwritten if it exists.</param>
    /// <param name="fileSizeBytes">Minimum file size; generation stops after the line that reaches it.</param>
    public PhraseGenerator(IEnumerable<string> words, string filePath, long fileSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileSizeBytes);

        _words = words.Select(w => w.Trim()).Where(w => w.Length > 0).ToArray();
        if (_words.Length == 0)
        {
            throw new ArgumentException("Words must contain at least one non-blank entry.", nameof(words));
        }

        _filePath = filePath;
        _fileSizeBytes = fileSizeBytes;
    }

    /// <param name="progress">Receives the number of bytes written so far.</param>
    public GenerateResult Generate(IProgress<double>? progress)
    {
        using var writer = new StreamWriter(_filePath, append: false, Utf8);
        var repeatPool = new List<string>(RepeatPoolCapacity);
        var reportEvery = Math.Max(1L, _fileSizeBytes / 100);
        var nextReport = reportEvery;
        var writtenBytes = 0L;
        var lines = 0L;
        var repeatedLines = 0L;

        while (writtenBytes < _fileSizeBytes)
        {
            string phrase;
            if (repeatPool.Count > 0 && _random.NextDouble() < RepeatProbability)
            {
                phrase = repeatPool[_random.Next(repeatPool.Count)];
                repeatedLines++;
            }
            else
            {
                phrase = BuildRandomPhrase();
                if (repeatPool.Count < RepeatPoolCapacity)
                {
                    repeatPool.Add(phrase);
                }
            }

            var line = $"{UniqueNumber(lines)}. {phrase}";
            writer.WriteLine(line);
            writtenBytes += Utf8.GetByteCount(line) + NewLineBytes;
            lines++;

            if (writtenBytes >= nextReport)
            {
                progress?.Report(Math.Min(writtenBytes, _fileSizeBytes));
                nextReport += reportEvery;
            }
        }

        progress?.Report(_fileSizeBytes);
        return new GenerateResult(lines, writtenBytes, repeatedLines);
    }

    private string BuildRandomPhrase()
    {
        var wordCount = _random.Next(1, 4);
        var phrase = new string[wordCount];
        for (var i = 0; i < wordCount; i++)
        {
            phrase[i] = _words[_random.Next(_words.Length)];
        }

        return string.Join(' ', phrase);
    }

    /// <summary>
    /// SplitMix64 finalizer: a bijection on 64-bit values, so every line index maps to a distinct,
    /// random-looking number.
    /// </summary>
    private static ulong UniqueNumber(long index)
    {
        unchecked
        {
            var x = (ulong)index + Seed;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
