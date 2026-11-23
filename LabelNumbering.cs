using System.Globalization;

namespace MeyertecPrint;

public static class LabelNumbering
{
    public const int MaximumCount = 100_000;
    public const int MaximumDigits = 19;

    public static long LastValue(long start, long step, int count)
    {
        if (count is < 1 or > MaximumCount || step == 0)
            throw new InvalidDataException(L10n.T("Numbering.InvalidSequence"));
        var last = (Int128)start + (Int128)step * (count - 1);
        if (last < long.MinValue || last > long.MaxValue)
            throw new InvalidDataException(L10n.T("Numbering.Overflow"));
        return (long)last;
    }

    public static string Format(long value, int minimumDigits, string prefix = "", string suffix = "")
    {
        if (minimumDigits is < 1 or > MaximumDigits)
            throw new InvalidDataException(L10n.T("Numbering.InvalidDigits"));
        return prefix + value.ToString("D" + minimumDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>Prepares a replacement batch without editing the source template.</summary>
    public static CsvBatch.Prepared Prepare(LabelLayout template, int targetIndex, int count, long start,
        long step, int minimumDigits, string prefix = "", string suffix = "")
    {
        LastValue(start, step, count);
        var targets = LabelPrintPages.GetBindings(template);
        if (targetIndex < 0 || targetIndex >= targets.Count || targets[targetIndex].Type == CellContentType.Image)
            throw new InvalidDataException(L10n.T("Numbering.SelectField"));
        var key = "C" + targetIndex.ToString(CultureInfo.InvariantCulture);
        var first = new Dictionary<string, string> { [key] = Format(start, minimumDigits, prefix, suffix) };
        var remaining = new List<LabelItem>(count - 1);
        var number = start;
        for (var i = 1; i < count; i++)
        {
            number += step;
            var value = Format(number, minimumDigits, prefix, suffix);
            remaining.Add(new LabelItem
            {
                Index = i + 1,
                Name = value,
                Fields = new Dictionary<string, string> { [key] = value }
            });
        }
        return new CsvBatch.Prepared(first, remaining);
    }
}
