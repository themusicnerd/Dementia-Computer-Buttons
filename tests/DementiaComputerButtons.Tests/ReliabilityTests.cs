using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public void ReconnectPrioritizesLastKnownPortButRetainsChangedPorts()
    {
        var candidates = SerialReconnectPolicy.OrderCandidates(["COM12", "COM4", "COM7", "COM4"], "COM7");
        Assert.Equal(["COM7", "COM4", "COM12"], candidates);
    }

    [Fact]
    public void ReconnectFallsBackWhenComNumberChanges()
    {
        var candidates = SerialReconnectPolicy.OrderCandidates(["COM9"], "COM3");
        Assert.Equal(["COM9"], candidates);
    }

    [Fact]
    public void HeartbeatExpiresOnlyAfterTimeout()
    {
        var last = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.False(HeartbeatHealth.IsExpired(last, last.AddSeconds(4), TimeSpan.FromSeconds(4)));
        Assert.True(HeartbeatHealth.IsExpired(last, last.AddMilliseconds(4001), TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public async Task MockControllerAllowsHardwareFreeDevelopment()
    {
        await using var mock = new MockArduinoService();
        await mock.StartAsync();
        await mock.SendCommandAsync("LED RED ON");
        Assert.True(mock.Connection.IsConnected);
        Assert.Contains("LED RED ON", mock.Commands);
    }
}
