namespace Xakpc.Tasks.LargeFileSort.Common;

/// <summary>
/// One line of the input file in the format <c>&lt;Number&gt;. &lt;String&gt;</c>.
/// </summary>
/// <remarks>
/// The original line is kept as-is, so the sorted output is byte-for-byte identical to the input lines.
/// <see cref="Number"/> and <see cref="Text"/> are views into it, so parsing allocates nothing.
/// </remarks>
internal readonly struct DataRow
{
    private const string Separator = ". ";

    private readonly int _separatorIndex;

    private DataRow(string line, int separatorIndex)
    {
        Line = line;
        _separatorIndex = separatorIndex;
    }

    public string Line { get; }

    /// <summary>Decimal digits only; may have leading zeros and be longer than any built-in integer type.</summary>
    public ReadOnlySpan<char> Number => Line.AsSpan(0, _separatorIndex);

    public ReadOnlySpan<char> Text => Line.AsSpan(_separatorIndex + Separator.Length);

    /// <summary>
    /// Parses a line. The number must be one or more ASCII digits (no sign, no whitespace),
    /// followed by the first <c>". "</c>; everything after it is the text, kept verbatim (it may contain <c>". "</c>).
    /// </summary>
    public static bool TryParse(string line, out DataRow row)
    {
        row = default;

        var separatorIndex = line.IndexOf(Separator, StringComparison.Ordinal);
        if (separatorIndex <= 0 || !IsAllAsciiDigits(line.AsSpan(0, separatorIndex)))
        {
            return false;
        }

        row = new DataRow(line, separatorIndex);
        return true;
    }

    private static bool IsAllAsciiDigits(ReadOnlySpan<char> value) =>
        !value.ContainsAnyExceptInRange('0', '9');
}
