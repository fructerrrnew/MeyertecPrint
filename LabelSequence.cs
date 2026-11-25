namespace MeyertecPrint;

public static class LabelSequence
{
    public static long CalculateFeedLengthUm(LabelLayout layout, int pageCount, long gapUm = 0)
    {
        var sections = LabelPrintPages.PhysicalSectionsPerLabel(layout);
        return CalculateLengthUm(layout.MediaHeightUm, checked(pageCount * sections), sections == 1 ? gapUm : 0);
    }

    /// <summary>Physical feed length in microns, including the gap between pages.</summary>
    public static long CalculateLengthUm(long mediaHeightUm, int pageCount, long gapUm = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mediaHeightUm);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(gapUm);
        return checked(mediaHeightUm * pageCount + gapUm * Math.Max(0, pageCount - 1));
    }
}
