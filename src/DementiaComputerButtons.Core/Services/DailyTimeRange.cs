namespace DementiaComputerButtons.Core.Services;

public static class DailyTimeRange
{
    public static bool Contains(bool enabled, string fromText, string untilText, TimeOnly now)
    {
        if (!enabled || !TimeOnly.TryParseExact(fromText, "HH:mm", out var from) ||
            !TimeOnly.TryParseExact(untilText, "HH:mm", out var until)) return false;
        if (from == until) return true;
        return from < until ? now >= from && now < until : now >= from || now < until;
    }
}
