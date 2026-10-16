namespace DementiaComputerButtons.Core.Services;

public static class ExifGpsConverter
{
    public static double Rational(ulong packed)
    {
        var numerator = packed >> 32;
        var denominator = packed & 0xffffffff;
        return denominator == 0 ? 0 : numerator / (double)denominator;
    }
}
