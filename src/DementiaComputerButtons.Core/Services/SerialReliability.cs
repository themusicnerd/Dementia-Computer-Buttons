namespace DementiaComputerButtons.Core.Services;

public static class SerialReconnectPolicy
{
    public static IReadOnlyList<string> OrderCandidates(IEnumerable<string> ports, string? lastKnownPort)
    {
        var unique = ports.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value.PadLeft(12, '0'), StringComparer.OrdinalIgnoreCase).ToList();
        if (lastKnownPort is null) return unique;
        var match = unique.FindIndex(value => value.Equals(lastKnownPort, StringComparison.OrdinalIgnoreCase));
        if (match > 0) { var preferred = unique[match]; unique.RemoveAt(match); unique.Insert(0, preferred); }
        return unique;
    }
}

public static class HeartbeatHealth
{
    public static bool IsExpired(DateTimeOffset lastHeartbeat, DateTimeOffset now, TimeSpan timeout) =>
        timeout <= TimeSpan.Zero || now - lastHeartbeat > timeout;
}
