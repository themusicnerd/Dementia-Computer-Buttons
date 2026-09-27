using System.Collections.Concurrent;
using System.Globalization;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Protocol;

namespace DementiaComputerButtons.Core.Services;

public sealed class ArduinoService(
    ISerialPortProvider portProvider,
    ISerialConnectionFactory connectionFactory,
    IConfigurationService configuration,
    IAsyncDelay delay,
    ILoggingService logging) : IArduinoService
{
    private readonly object _connectionSync = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private ISerialConnection? _serial;
    private int _requestId;
    private int _communicationErrors;
    private DateTimeOffset _lastPongUtc;
    private string? _lastKnownPort;
    private volatile bool _controllerRestarted;

    public ArduinoConnectionSnapshot Connection { get; private set; } = new(false, StatusDetail: "Not started");
    public event EventHandler<ArduinoConnectionSnapshot>? ConnectionChanged;
    public event EventHandler<ButtonEvent>? ButtonChanged;
    public event EventHandler<SensorSnapshot>? SensorsChanged;
    public event EventHandler<string>? MessageReceived;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_worker is not null) return Task.CompletedTask;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = Task.Run(() => DiscoveryLoopAsync(_lifetime.Token), CancellationToken.None);
        logging.Information("arduino_service_start", "Serial discovery started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is null) return;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_worker is not null)
        {
            try { await _worker.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        Disconnect("Stopped");
        _worker = null;
        _lifetime.Dispose();
        _lifetime = null;
    }

    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var ports = SerialReconnectPolicy.OrderCandidates(portProvider.GetPortNames(), _lastKnownPort);
            if (ports.Count == 0) UpdateConnection(Connection with { IsConnected = false, PortName = null, StatusDetail = "No serial ports found" });

            foreach (var portName in ports)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    logging.Information("com_probe", $"Probing {portName}");
                    if (await TryConnectAsync(portName, cancellationToken).ConfigureAwait(false))
                    {
                        await ConnectedLoopAsync(cancellationToken).ConfigureAwait(false);
                        Disconnect("Controller disconnected; scanning");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException)
                {
                    Interlocked.Increment(ref _communicationErrors);
                    logging.Warning("com_probe_failed", $"{portName}: {exception.Message}");
                    Disconnect($"{portName}: {exception.Message}");
                }
            }

            await delay.Delay(TimeSpan.FromMilliseconds(configuration.Current.Controller.ReconnectDelayMs), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryConnectAsync(string portName, CancellationToken cancellationToken)
    {
        ISerialConnection? candidate = null;
        try
        {
            candidate = connectionFactory.Create(portName, configuration.Current.Controller.BaudRate);
            candidate.Open();
            // Opening the Uno-compatible port resets the ATmega328P. Wait for its bootloader.
            await delay.Delay(TimeSpan.FromMilliseconds(1700), cancellationToken).ConfigureAwait(false);
            candidate.WriteLine("HELLO DCB/1");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = candidate.TryReadLine();
                if (line is null) continue;
                var message = ProtocolParser.Parse(line);
                if (message.Kind != ControllerMessageKind.Hello) continue;
                var fields = message.Fields!;
                if (!fields.TryGetValue("board", out var board) ||
                    !board.Equals(configuration.Current.Controller.ExpectedBoard, StringComparison.OrdinalIgnoreCase))
                    continue;

                lock (_connectionSync) _serial = candidate;
                candidate = null;
                _lastPongUtc = DateTimeOffset.UtcNow;
                _lastKnownPort = portName;
                _controllerRestarted = false;
                UpdateConnection(new(true, portName, board,
                    fields.GetValueOrDefault("firmware"), fields.GetValueOrDefault("protocol", "DCB/1"),
                    LastHeartbeatUtc: _lastPongUtc, LastMessage: line,
                    CommunicationErrors: _communicationErrors, StatusDetail: "Connected and healthy",
                    PanelButtonsAvailable: fields.GetValueOrDefault("pb") == "1",
                    PanelLedsAvailable: fields.GetValueOrDefault("pl") == "1"));
                logging.Information("arduino_handshake", $"port={portName}; board={board}; firmware={Connection.FirmwareVersion}");
                return true;
            }
            return false;
        }
        finally
        {
            if (candidate is not null) { candidate.Close(); candidate.Dispose(); }
        }
    }

    private async Task ConnectedLoopAsync(CancellationToken cancellationToken)
    {
        var heartbeatInterval = TimeSpan.FromMilliseconds(configuration.Current.Controller.HeartbeatIntervalMs);
        var heartbeatTimeout = TimeSpan.FromMilliseconds(configuration.Current.Controller.HeartbeatTimeoutMs);
        var nextHeartbeat = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested && GetSerial() is { IsOpen: true } serial)
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextHeartbeat)
            {
                await WriteAsync($"PING {Environment.TickCount64}", cancellationToken).ConfigureAwait(false);
                nextHeartbeat = now + heartbeatInterval;
            }

            var line = serial.TryReadLine();
            if (line is not null) ProcessIncoming(line);
            if (_controllerRestarted) throw new IOException("Controller restarted; re-establishing handshake");
            if (HeartbeatHealth.IsExpired(_lastPongUtc, DateTimeOffset.UtcNow, heartbeatTimeout))
                throw new TimeoutException("Controller heartbeat timed out");
            await Task.Yield();
        }
    }

    private void ProcessIncoming(string line)
    {
        MessageReceived?.Invoke(this, line);
        var message = ProtocolParser.Parse(line);
        var uptime = message.Fields is not null && message.Fields.TryGetValue("uptime", out var uptimeText) &&
                     long.TryParse(uptimeText, CultureInfo.InvariantCulture, out var parsedUptime) ? parsedUptime : Connection.UptimeMilliseconds;
        UpdateConnection(Connection with { LastMessage = line, UptimeMilliseconds = uptime, CommunicationErrors = _communicationErrors });

        switch (message.Kind)
        {
            case ControllerMessageKind.Ready:
                _controllerRestarted = true;
                logging.Warning("controller_restart", line);
                break;
            case ControllerMessageKind.Pong:
                _lastPongUtc = DateTimeOffset.UtcNow;
                UpdateConnection(Connection with { LastHeartbeatUtc = _lastPongUtc, StatusDetail = "Connected and healthy" });
                break;
            case ControllerMessageKind.Acknowledgement:
                if (message.RequestId is not null && _pending.TryRemove(message.RequestId, out var completion)) completion.TrySetResult(true);
                break;
            case ControllerMessageKind.Data:
                if (message.RequestId is not null && _pending.TryRemove(message.RequestId, out var dataCompletion)) dataCompletion.TrySetResult(true);
                break;
            case ControllerMessageKind.Error:
                Interlocked.Increment(ref _communicationErrors);
                if (message.RequestId is not null && _pending.TryRemove(message.RequestId, out var failed))
                    failed.TrySetException(new InvalidOperationException(message.Error));
                logging.Warning("controller_error", line);
                break;
            case ControllerMessageKind.ButtonEvent when message.Button is not null:
                ButtonChanged?.Invoke(this, message.Button);
                break;
            case ControllerMessageKind.Telemetry when message.Sensors is not null:
                SensorsChanged?.Invoke(this, message.Sensors);
                break;
            case ControllerMessageKind.Malformed:
                Interlocked.Increment(ref _communicationErrors);
                logging.Warning("protocol_malformed", line);
                break;
        }
    }

    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!Connection.IsConnected) throw new InvalidOperationException("Arduino controller is not connected.");
        var id = Interlocked.Increment(ref _requestId).ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate command identifier.");
        try
        {
            await WriteAsync($"CMD {id} {command}", cancellationToken).ConfigureAwait(false);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task WriteAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var serial = GetSerial() ?? throw new IOException("Serial connection is unavailable.");
            serial.WriteLine(line);
        }
        finally { _writeLock.Release(); }
    }

    private ISerialConnection? GetSerial() { lock (_connectionSync) return _serial; }

    private void Disconnect(string detail)
    {
        ISerialConnection? serial;
        lock (_connectionSync) { serial = _serial; _serial = null; }
        if (serial is not null) { try { serial.Close(); } catch { } serial.Dispose(); }
        foreach (var pending in _pending.Values) pending.TrySetException(new IOException("Controller disconnected."));
        _pending.Clear();
        UpdateConnection(Connection with { IsConnected = false, PortName = null, StatusDetail = detail, CommunicationErrors = _communicationErrors });
    }

    private void UpdateConnection(ArduinoConnectionSnapshot snapshot)
    {
        Connection = snapshot;
        ConnectionChanged?.Invoke(this, snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _writeLock.Dispose();
    }
}
