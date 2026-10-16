namespace DementiaComputerButtons.Core.Services;

public static class VolumeStepCalculator
{
    public static float Next(float currentPercent, int direction, int stepPercent)
    {
        var current = Math.Clamp(currentPercent, 0, 100);
        var step = Math.Clamp(stepPercent, 1, 25);
        if (direction == 0) return current;
        var target = direction > 0
            ? (Math.Floor((current + 0.05f) / step) + 1) * step
            : (Math.Ceiling((current - 0.05f) / step) - 1) * step;
        return (float)Math.Clamp(target, 0, 100);
    }
}
