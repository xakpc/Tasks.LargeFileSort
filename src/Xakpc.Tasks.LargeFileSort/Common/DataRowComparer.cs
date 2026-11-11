namespace Xakpc.Tasks.LargeFileSort.Common;

/// <summary>
/// Orders rows by <see cref="DataRow.Text"/>, then by <see cref="DataRow.Number"/> ascending.
/// </summary>
/// <remarks>
/// Text is compared ordinally (by UTF-16 code unit): culture-independent, deterministic and fast.
/// As a consequence uppercase letters sort before lowercase ("Zebra" &lt; "apple").
/// Numbers are compared by numeric value without parsing, so any length is supported.
/// </remarks>
internal sealed class DataRowComparer : IComparer<DataRow>
{
    public static DataRowComparer Instance { get; } = new();

    public int Compare(DataRow x, DataRow y)
    {
        var textComparison = x.Text.SequenceCompareTo(y.Text);
        return textComparison != 0 ? textComparison : CompareNumbers(x.Number, y.Number);
    }

    private static int CompareNumbers(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        x = x.TrimStart('0');
        y = y.TrimStart('0');

        // without leading zeros, a longer number is a bigger number; same length compares digit by digit
        return x.Length != y.Length
            ? x.Length.CompareTo(y.Length)
            : x.SequenceCompareTo(y);
    }
}
