using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Core.Services;

public sealed class MockArduinoService : IArduinoService
{
    private readonly List<string> _commands = [];
    private Timer? _telemetryTimer;
    private long _startedAt;
    public ArduinoConnectionSnapshot Connection { get; private set; } = new(false, StatusDetail: "Mock stopped");
    public IReadOnlyList<string> Commands { get { lock (_commands) return _commands.ToArray(); } }
    public event EventHandler<ArduinoConnectionSnapshot>? ConnectionChanged;
    public event EventHandler<ButtonEvent>? ButtonChanged;
    public event EventHandler<SensorSnapshot>? SensorsChanged;
    public event EventHandler<string>? MessageReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _startedAt = Environment.TickCount64;
        Connection = new(true, "MOCK", "KS0501", "mock-1.0", "DCB/1", LastHeartbeatUtc: DateTimeOffset.UtcNow,
            LastMessage: "HELLO DCB/1 KS0501 mock-1.0", StatusDetail: "Mock controller connected");
        ConnectionChanged?.Invoke(this, Connection);
        _telemetryTimer = new Timer(_ => EmitTelemetry(), null, 250, 1000);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _telemetryTimer?.Dispose();
        _telemetryTimer = null;
        Connection = Connection with { IsConnected = false, PortName = null, StatusDetail = "Mock stopped" };
        ConnectionChanged?.Invoke(this, Connection);
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!Connection.IsConnected) throw new InvalidOperationException("Mock controller is stopped.");
        lock (_commands) _commands.Add(command);
        MessageReceived?.Invoke(this, $"ACK mock-{_commands.Count}");
        return Task.CompletedTask;
    }

    public void SimulateButton(string name, string action = "DOWN") =>
        ButtonChanged?.Invoke(this, new(name, action, Environment.TickCount64 - _startedAt));

    private void EmitTelemetry()
    {
        var uptime = Environment.TickCount64 - _startedAt;
        var sensors = new SensorSnapshot(420, 12, uptime);
        SensorsChanged?.Invoke(this, sensors);
        Connection = Connection with { UptimeMilliseconds = uptime, LastHeartbeatUtc = DateTimeOffset.UtcNow };
        ConnectionChanged?.Invoke(this, Connection);
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
