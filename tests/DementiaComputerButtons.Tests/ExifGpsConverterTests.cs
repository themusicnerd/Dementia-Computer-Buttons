using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class ExifGpsConverterTests
{
    [Fact]
    public void DecodesWicUnsignedRationalWithNumeratorInHighBits()
    {
        var packed = ((ulong)5683 << 32) | 100;

        Assert.Equal(56.83, ExifGpsConverter.Rational(packed), 5);
    }

    [Fact]
    public void ZeroDenominatorIsSafe() => Assert.Equal(0, ExifGpsConverter.Rational((ulong)25 << 32));
}
