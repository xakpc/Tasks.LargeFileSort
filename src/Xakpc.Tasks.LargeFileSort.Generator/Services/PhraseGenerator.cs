using System.Diagnostics;

namespace Xakpc.Tasks.LargeFileSort.Generator.Services;

interface IPhraseGenerator
{
    /// <summary>
    /// Generate test file with random words and phrases, reporting progress as a percentage of the total file size.
    /// </summary>
    GenerateResult Generate(IProgress<double> progress);
}

internal class PhraseGenerator : IPhraseGenerator
{
    private readonly string _filePath;
    private readonly string[] _words;
    private readonly ulong _fileSizeBytes;
    private readonly Random _random;

    const int Seed = 46;

    public PhraseGenerator(string[] words, string filePath, ulong fileSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(words, nameof(words));
        if (words.Length==0)
        {
            throw new ArgumentException("Words array cannot be empty.", nameof(words));
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fileSizeBytes, 0UL, nameof(fileSizeBytes));

        _filePath = filePath;
        _words = words;
        _fileSizeBytes = fileSizeBytes;
        _random = new Random(Seed);
    }

    public GenerateResult Generate(IProgress<double> progress)
    {
        using var streamWriter = new StreamWriter(_filePath, append: false);
        var progressStep = _fileSizeBytes / 100.0; // Update progress every 1% of the file size
        var currentProgress = 0.0;
        var currentFileSize = 0UL;
        var step = 0UL;
        var dupesCount = 0;

        const int repeatWordsPoolCapacity = 10000;
        var randomWordsToRepeat = new List<string>(repeatWordsPoolCapacity);

        while (currentFileSize < _fileSizeBytes)
        {
            var number = PermutateRandomUniqueNumber(step, Seed);
            string randomWord;

            // choose a random word from the pool of previously generated words to repeat
            if (randomWordsToRepeat.Count > 0 && _random.NextDouble() < 0.1) // 10% chance to repeat a word
            {
                dupesCount++;
                randomWord = $"{number}. " + randomWordsToRepeat[_random.Next(randomWordsToRepeat.Count)];
            }
            else
            {
                var randomPhrase = BuildRandomPhrase(_words);

                if (randomWordsToRepeat.Count < repeatWordsPoolCapacity)
                {
                    randomWordsToRepeat.Add(randomPhrase);
                }

                randomWord = $"{number}. {randomPhrase}";
            }

            streamWriter.Write(randomWord + Environment.NewLine);

            currentFileSize += (ulong)(randomWord.Length + Environment.NewLine.Length);
            step++;

            currentProgress = UpdateProgress(progress, progressStep, currentProgress, currentFileSize);
        }

        // report 100%
        progress.Report(currentFileSize);
        return new GenerateResult(step, currentFileSize, dupesCount);

        static double UpdateProgress(IProgress<double> progress, double progressStep, double currentProgress, 
            ulong currentFileSize)
        {
            // report progress if we've crossed the next progress step
            if (currentFileSize > currentProgress + progressStep)
            {
                Debug.WriteLine($"Current file size: {currentFileSize} bytes, Progress: {currentProgress + progressStep} bytes");
                currentProgress += progressStep;
                progress.Report(currentProgress);
            }

            return currentProgress;
        }
    }

    string BuildRandomPhrase(string[] words)
    {
        var length = _random.Next(1, 4);

        var phrase = new List<string>();

        for (int i = 0; i < length; i++)
        {
            phrase.Add(words[_random.Next(words.Length)]);
        }

        return string.Join(" ", phrase);
    }

    /// <summary>
    /// SplitMix64 permutation to get unique number based on index
    /// </summary>
    static ulong PermutateRandomUniqueNumber(ulong index, ulong seed)
    {
        unchecked
        {
            ulong x = index + seed;

            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;

            return x ^ (x >> 31);
        }
    }
}
