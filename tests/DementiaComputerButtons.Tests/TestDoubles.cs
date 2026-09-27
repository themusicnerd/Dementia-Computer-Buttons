using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Tests;

internal sealed class TestLog : ILoggingService
{
    public List<string> Entries { get; } = [];
    public void Information(string eventName, string message) => Entries.Add($"I:{eventName}:{message}");
    public void Warning(string eventName, string message) => Entries.Add($"W:{eventName}:{message}");
    public void Error(string eventName, Exception exception, string message) => Entries.Add($"E:{eventName}:{message}");
}

internal sealed class TestVolume : IVolumeService
{
    public event EventHandler<float>? VolumeChanged;
    public float CurrentVolumePercent { get; private set; } = 50;
    public bool IsMuted { get; private set; }
    public void Refresh() { }
    public void ChangeBy(float deltaPercent) => SetVolume(CurrentVolumePercent + deltaPercent);
    public void SetVolume(float percent) { CurrentVolumePercent = Math.Clamp(percent, 0, 100); VolumeChanged?.Invoke(this, CurrentVolumePercent); }
    public void ToggleMute() => IsMuted = !IsMuted;
}

internal sealed class TestArduino : IArduinoService
{
    public ArduinoConnectionSnapshot Connection { get; } = new(true, "MOCK", "KS0501", "test");
    public List<string> Commands { get; } = [];
    public event EventHandler<ArduinoConnectionSnapshot>? ConnectionChanged { add { } remove { } }
    public event EventHandler<ButtonEvent>? ButtonChanged { add { } remove { } }
    public event EventHandler<SensorSnapshot>? SensorsChanged { add { } remove { } }
    public event EventHandler<string>? MessageReceived { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SendCommandAsync(string command, CancellationToken cancellationToken = default) { Commands.Add(command); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class TestConfiguration(AppConfiguration value) : IConfigurationService
{
    public AppConfiguration Current { get; } = value;
    public IReadOnlyList<string> Warnings => [];
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ImportAsync(string sourcePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class TestAudioRouting : IAudioRoutingService
{
    public bool IsAvailable { get; set; } = true;
    public bool SpeakersMuted { get; private set; }
    public event EventHandler<bool>? SpeakersMutedChanged;
    public IReadOnlyList<AudioOutputDevice> GetOutputDevices() =>
        [new("speakers", "Test Speakers"), new("headphones", "Test Headphones")];
    public IReadOnlyList<AudioOutputDevice> GetInputDevices() =>
        [new("speaker-mic", "Test Room Microphone"), new("headset-mic", "Test Headset Microphone")];
    public AudioLevelSnapshot GetLevels() => new(0, 0);
    public Task RestoreHomeRoutingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> ToggleSpeakersAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return Task.FromResult(false);
        SpeakersMuted = !SpeakersMuted;
        SpeakersMutedChanged?.Invoke(this, SpeakersMuted);
        return Task.FromResult(true);
    }
}

internal sealed class TestVlc : IVlcService
{
    public string? PlayedPath { get; private set; }
    public int StopCount { get; private set; }
    public bool IsPlaying { get; private set; }
    public event EventHandler? PlaybackEnded;
    public Task PlayAsync(string path, CancellationToken cancellationToken = default) { PlayedPath = path; IsPlaying = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken = default) { StopCount++; IsPlaying = false; return Task.CompletedTask; }
    public void Finish() { IsPlaying = false; PlaybackEnded?.Invoke(this, EventArgs.Empty); }
}

internal sealed class TestBrowser : IBrowserService
{
    public string? OpenedUrl { get; private set; }
    public int CloseCount { get; private set; }
    public Task OpenAsync(string url, CancellationToken cancellationToken = default) { OpenedUrl = url; return Task.CompletedTask; }
    public Task CloseManagedContentAsync(CancellationToken cancellationToken = default) { CloseCount++; return Task.CompletedTask; }
}

internal sealed class TestCalls : ICallService
{
    public CallSnapshot Current { get; private set; } = new(CallStatus.Idle);
    public bool IsActive => Current.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected;
    public bool IsCallingBlocked { get; set; }
    public event EventHandler<CallSnapshot>? StatusChanged;
    public string? Contact { get; private set; }
    public int EndCount { get; private set; }
    public int AnswerCount { get; private set; }
    public int RejectCount { get; private set; }
    public Task StartCallAsync(string contactKey, CancellationToken cancellationToken = default) { Contact = contactKey; Current = new(CallStatus.Calling, contactKey); return Task.CompletedTask; }
    public Task AnswerIncomingAsync(CancellationToken cancellationToken = default) { AnswerCount++; Current = Current with { Status = CallStatus.Connected }; StatusChanged?.Invoke(this, Current); return Task.CompletedTask; }
    public Task RejectIncomingAsync(CancellationToken cancellationToken = default) { RejectCount++; Current = Current with { Status = CallStatus.Ended }; StatusChanged?.Invoke(this, Current); return Task.CompletedTask; }
    public Task EndCallAsync(CancellationToken cancellationToken = default) { if (!IsActive) return Task.CompletedTask; EndCount++; Current = Current with { Status = CallStatus.Ended }; return Task.CompletedTask; }
    public string ConfigureMicroSipIntegration(string applicationPath) => "test";
    public void HandleExternalEvent(string eventName, string callerId) { Current = new(eventName == "INCOMING" ? CallStatus.Incoming : CallStatus.Ended, callerId, callerId); StatusChanged?.Invoke(this, Current); }
}

internal sealed class TestOsd : IOnScreenDisplayService
{
    public string? Headline { get; private set; }
    public string? Detail { get; private set; }
    public void ShowMessage(string headline, string? detail = null) { Headline = headline; Detail = detail; }
}

internal sealed class TestWindowsShell : IWindowsShellService
{
    public int CloseCount { get; private set; }
    public bool CloseStartMenuIfOpen() { CloseCount++; return true; }
}

internal sealed class TestDisplaySchedule : IDisplayScheduleService
{
    public int RearmCount { get; private set; }
    public int BlackoutCount { get; private set; }
    public bool IsBlackoutActive { get; set; }
    public event EventHandler<bool>? BlackoutStateChanged;
    public void BlackoutNow() => BlackoutCount++;
    public void RearmBlackout() => RearmCount++;
    public void SetBlackout(bool active)
    {
        IsBlackoutActive = active;
        BlackoutStateChanged?.Invoke(this, active);
    }
}
