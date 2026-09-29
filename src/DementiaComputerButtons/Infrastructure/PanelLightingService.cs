using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Infrastructure;

public sealed class PanelLightingService(
    IArduinoService arduino,
    ISystemStateService state,
    IAudioRoutingService audio,
    ICallService calls,
    IDisplayScheduleService displaySchedule,
    IConfigurationService configuration,
    ILoggingService logging)
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private bool _panelOnline;
    private byte? _lastBrightness;
    private CancellationTokenSource? _ringing;
    private bool[] _currentLightStates = new bool[6];

    public IReadOnlyList<bool> CurrentLightStates => _currentLightStates;
    public event EventHandler<IReadOnlyList<bool>>? LightStatesChanged;

    public void Start()
    {
        arduino.ConnectionChanged += OnConnectionChanged;
        state.StateChanged += (_, _) => _ = SynchronizeAsync();
        audio.SpeakersMutedChanged += (_, _) => _ = SynchronizeAsync();
        calls.StatusChanged += (_, snapshot) => OnCallStatusChanged(snapshot);
        displaySchedule.BlackoutStateChanged += (_, _) => _ = SynchronizeAsync();
        OnConnectionChanged(this, arduino.Connection);
        _ = SynchronizeAsync();
    }

    private void OnCallStatusChanged(CallSnapshot snapshot)
    {
        _ringing?.Cancel();
        _ringing?.Dispose();
        _ringing = null;
        if (snapshot.Status == CallStatus.Incoming)
        {
            _ringing = new CancellationTokenSource();
            _ = FlashAllAsync(_ringing.Token);
        }
        else _ = SynchronizeAsync();
    }

    private async Task FlashAllAsync(CancellationToken cancellationToken)
    {
        var on = true;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PublishLightStates(Enumerable.Repeat(on, 6).ToArray());
                if (_panelOnline)
                {
                    await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        for (var index = 0; index < 6; ++index)
                            await arduino.SendCommandAsync($"PANEL LED {index} {(on ? "ON" : "OFF")}", cancellationToken).ConfigureAwait(false);
                    }
                    finally { _sync.Release(); }
                }
                on = !on;
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { logging.Error("panel_ringing_flash_failed", exception, exception.Message); }
    }

    private void OnConnectionChanged(object? sender, ArduinoConnectionSnapshot connection)
    {
        var available = connection.IsConnected && connection.PanelLedsAvailable;
        if (available && !_panelOnline) { _panelOnline = true; _ = SynchronizeAsync(); }
        else if (!available) _panelOnline = false;
    }

    private async Task SynchronizeAsync()
    {
        if (calls.Current.Status == CallStatus.Incoming) return;
        var states = new[]
        {
            true, true,
            state.Current is DadConsoleState.WatchingTV or DadConsoleState.ListeningSpotify,
            state.Current == DadConsoleState.PlayingVideo,
            true,
            audio.IsAvailable && audio.SpeakersMuted
        };
        PublishLightStates(states);
        if (!_panelOnline) return;
        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Version.TryParse(arduino.Connection.FirmwareVersion, out var firmwareVersion) &&
                firmwareVersion >= new Version(0, 2, 2))
                await arduino.SendCommandAsync($"PANEL LONGPRESS {configuration.Current.Controller.LongPressMilliseconds}").ConfigureAwait(false);
            var configuredBrightness = configuration.Current.Lighting.PanelBrightnessPercent;
            // One percent maps to the lowest non-zero level in the controller's 16-step software PWM.
            var brightness = displaySchedule.IsBlackoutActive && configuredBrightness > 0 ? (byte)1 : configuredBrightness;
            await arduino.SendCommandAsync($"PANEL BRIGHTNESS {brightness}").ConfigureAwait(false);
            if (_lastBrightness != brightness)
            {
                _lastBrightness = brightness;
                logging.Information("panel_brightness", $"Panel brightness set to {brightness}% (blackout={displaySchedule.IsBlackoutActive})");
            }
            for (var index = 0; index < states.Length; ++index)
                await arduino.SendCommandAsync($"PANEL LED {index} {(states[index] ? "ON" : "OFF")}").ConfigureAwait(false);
        }
        catch (Exception exception) { logging.Error("panel_lighting_sync_failed", exception, exception.Message); }
        finally { _sync.Release(); }
    }

    private void PublishLightStates(bool[] states)
    {
        if (_currentLightStates.SequenceEqual(states)) return;
        _currentLightStates = states;
        LightStatesChanged?.Invoke(this, states);
    }
}
