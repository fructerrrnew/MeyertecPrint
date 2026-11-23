namespace MeyertecPrint;

public static class LabelSequence
{
    /// <summary>Physical feed length in microns, including the gap between pages.</summary>
    public static long CalculateLengthUm(long mediaHeightUm, int pageCount, long gapUm = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mediaHeightUm);
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        ArgumentOutOfRangeException.ThrowIfNegative(gapUm);
        return checked(mediaHeightUm * pageCount + gapUm * Math.Max(0, pageCount - 1));
    }
}
