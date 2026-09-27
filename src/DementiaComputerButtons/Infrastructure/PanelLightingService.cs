using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Infrastructure;

public sealed class PanelLightingService(
    IArduinoService arduino,
    ISystemStateService state,
    IAudioRoutingService audio,
    ICallService calls,
    IConfigurationService configuration,
    ILoggingService logging)
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private bool _panelOnline;
    private CancellationTokenSource? _ringing;

    public void Start()
    {
        arduino.ConnectionChanged += OnConnectionChanged;
        state.StateChanged += (_, _) => _ = SynchronizeAsync();
        audio.SpeakersMutedChanged += (_, _) => _ = SynchronizeAsync();
        calls.StatusChanged += (_, snapshot) => OnCallStatusChanged(snapshot);
        OnConnectionChanged(this, arduino.Connection);
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
        if (!_panelOnline) return;
        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            await arduino.SendCommandAsync($"PANEL BRIGHTNESS {configuration.Current.Lighting.PanelBrightnessPercent}").ConfigureAwait(false);
            var states = new[]
            {
                true, true,
                state.Current is DadConsoleState.WatchingTV or DadConsoleState.ListeningSpotify,
                state.Current == DadConsoleState.PlayingVideo,
                true,
                audio.IsAvailable && audio.SpeakersMuted
            };
            for (var index = 0; index < states.Length; ++index)
                await arduino.SendCommandAsync($"PANEL LED {index} {(states[index] ? "ON" : "OFF")}").ConfigureAwait(false);
        }
        catch (Exception exception) { logging.Error("panel_lighting_sync_failed", exception, exception.Message); }
        finally { _sync.Release(); }
    }
}
