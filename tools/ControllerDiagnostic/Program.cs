using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;

var log = new ConsoleLog();
var configuration = new InMemoryConfiguration();
await using var controller = new ArduinoService(new SerialPortProvider(), new SerialConnectionFactory(),
    configuration, new SystemAsyncDelay(), log);
var connected = new TaskCompletionSource<ArduinoConnectionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
controller.ConnectionChanged += (_, value) =>
{
    Console.WriteLine($"STATUS connected={value.IsConnected} port={value.PortName ?? "-"} detail={value.StatusDetail}");
    if (value.IsConnected) connected.TrySetResult(value);
};
controller.SensorsChanged += (_, value) =>
    Console.WriteLine($"SENSORS light={value.AmbientLight} mic={value.Microphone} uptime={value.UptimeMilliseconds}");
controller.ButtonChanged += (_, value) =>
    Console.WriteLine($"BUTTON {value.Button} {value.Action} uptime={value.UptimeMilliseconds}");

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
try
{
    await controller.StartAsync(timeout.Token);
    var identity = await connected.Task.WaitAsync(timeout.Token);
    Console.WriteLine($"IDENTITY protocol={identity.ProtocolVersion} board={identity.Board} firmware={identity.FirmwareVersion} panelButtons={identity.PanelButtonsAvailable} panelLeds={identity.PanelLedsAvailable}");
    await controller.SendCommandAsync("GET STATUS", timeout.Token);
    await controller.SendCommandAsync("PANEL ALL OFF", timeout.Token);
    await controller.SendCommandAsync("PANEL LED 3 ON", timeout.Token);
    await controller.SendCommandAsync("PANEL BRIGHTNESS 25", timeout.Token);
    await Task.Delay(200, timeout.Token);
    await controller.SendCommandAsync("PANEL BRIGHTNESS 100", timeout.Token);
    await controller.SendCommandAsync("PANEL LED 3 OFF", timeout.Token);
    await controller.SendCommandAsync("TELEMETRY 500", timeout.Token);
    await Task.Delay(1700, timeout.Token);
    Console.WriteLine("RESULT PASS handshake, panel ON/OFF/brightness commands, heartbeat and telemetry");
}
catch (Exception exception)
{
    Console.Error.WriteLine($"RESULT FAIL {exception.Message}");
    Environment.ExitCode = 1;
}
finally { await controller.StopAsync(); }

file sealed class ConsoleLog : ILoggingService
{
    public void Information(string eventName, string message) => Console.WriteLine($"INFO {eventName} {message}");
    public void Warning(string eventName, string message) => Console.WriteLine($"WARN {eventName} {message}");
    public void Error(string eventName, Exception exception, string message) => Console.Error.WriteLine($"ERROR {eventName} {message}");
}

file sealed class InMemoryConfiguration : IConfigurationService
{
    public AppConfiguration Current { get; } = new();
    public IReadOnlyList<string> Warnings => [];
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
