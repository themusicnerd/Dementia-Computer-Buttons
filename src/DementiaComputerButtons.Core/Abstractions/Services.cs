using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Core.Abstractions;

public interface IArduinoService : IAsyncDisposable
{
    ArduinoConnectionSnapshot Connection { get; }
    event EventHandler<ArduinoConnectionSnapshot>? ConnectionChanged;
    event EventHandler<ButtonEvent>? ButtonChanged;
    event EventHandler<SensorSnapshot>? SensorsChanged;
    event EventHandler<string>? MessageReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendCommandAsync(string command, CancellationToken cancellationToken = default);
}

public interface IVolumeService
{
    event EventHandler<float>? VolumeChanged;
    float CurrentVolumePercent { get; }
    bool IsMuted { get; }
    void Refresh();
    void ChangeBy(float deltaPercent);
    void SetVolume(float percent);
    void ToggleMute();
}

public interface ISystemStateService
{
    DadConsoleState Current { get; }
    event EventHandler<DadConsoleState>? StateChanged;
    void TransitionTo(DadConsoleState target, string reason);
    void ReturnHome(string reason);
}

public interface IButtonActionService
{
    Task HandleAsync(ButtonEvent buttonEvent, CancellationToken cancellationToken = default);
}

public interface IOnScreenDisplayService
{
    void ShowMessage(string headline, string? detail = null);
}

public interface IConfigurationService
{
    AppConfiguration Current { get; }
    IReadOnlyList<string> Warnings { get; }
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public interface IAudioRoutingService
{
    bool IsAvailable { get; }
    bool SpeakersMuted { get; }
    IReadOnlyList<AudioOutputDevice> GetOutputDevices();
    IReadOnlyList<AudioOutputDevice> GetInputDevices();
    AudioLevelSnapshot GetLevels();
    event EventHandler<bool>? SpeakersMutedChanged;
    Task RestoreHomeRoutingAsync(CancellationToken cancellationToken = default);
    Task<bool> ToggleSpeakersAsync(CancellationToken cancellationToken = default);
}
public interface IVlcService
{
    bool IsPlaying { get; }
    event EventHandler? PlaybackEnded;
    Task PlayAsync(string path, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
public interface IBrowserService { Task OpenAsync(string url, CancellationToken cancellationToken = default); Task CloseManagedContentAsync(CancellationToken cancellationToken = default); }
public interface ICallService
{
    CallSnapshot Current { get; }
    bool IsActive { get; }
    bool IsCallingBlocked { get; }
    event EventHandler<CallSnapshot>? StatusChanged;
    Task StartCallAsync(string contactKey, CancellationToken cancellationToken = default);
    Task AnswerIncomingAsync(CancellationToken cancellationToken = default);
    Task RejectIncomingAsync(CancellationToken cancellationToken = default);
    Task EndCallAsync(CancellationToken cancellationToken = default);
    string ConfigureMicroSipIntegration(string applicationPath);
    void HandleExternalEvent(string eventName, string callerId);
}
public interface IWindowsShellService
{
    bool CloseStartMenuIfOpen();
}
public interface IStartupService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
public interface IUpdateService
{
    Version CurrentVersion { get; }
    Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default);
    Task DownloadAndInstallAsync(UpdateRelease release, CancellationToken cancellationToken = default);
}
public interface IDisplayScheduleService
{
    bool IsBlackoutActive { get; }
    event EventHandler<bool>? BlackoutStateChanged;
    void BlackoutNow();
    void RearmBlackout();
}
public interface IApplicationManagerService
{
    IReadOnlyList<ManagedApplication> Scan();
    Task<bool> InstallAsync(string key, CancellationToken cancellationToken = default);
}
public interface IFirmwareUpdateService
{
    string BundledVersion { get; }
    bool IsToolAvailable { get; }
    Task UpdateControllerAsync(CancellationToken cancellationToken = default);
}
public interface IIRService { bool IsHardwareAvailable { get; } }

public interface ILoggingService
{
    void Information(string eventName, string message);
    void Warning(string eventName, string message);
    void Error(string eventName, Exception exception, string message);
}
