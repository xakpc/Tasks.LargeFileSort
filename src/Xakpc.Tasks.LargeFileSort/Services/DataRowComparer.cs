namespace Xakpc.Tasks.LargeFileSort.Services;

internal class DataRowComparer : IComparer<DataRow>
{
    public int Compare(DataRow x, DataRow y)
    {
        var textComparison = string.Compare(x.Text, y.Text, StringComparison.Ordinal);
        if (textComparison != 0)
        {
            return textComparison;
        }
        return x.Number.CompareTo(y.Number);
    }
}
