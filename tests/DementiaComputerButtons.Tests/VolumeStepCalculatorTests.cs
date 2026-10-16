using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class VolumeStepCalculatorTests
{
    [Theory]
    [InlineData(23, 1, 25)]
    [InlineData(23, -1, 20)]
    [InlineData(25, 1, 30)]
    [InlineData(25, -1, 20)]
    [InlineData(99, 1, 100)]
    [InlineData(1, -1, 0)]
    public void SnapsInPressedDirection(float current, int direction, float expected) =>
        Assert.Equal(expected, VolumeStepCalculator.Next(current, direction, 5));
}
