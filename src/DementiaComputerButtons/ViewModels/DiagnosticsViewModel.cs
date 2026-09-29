using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Infrastructure;
using Microsoft.Win32;

namespace DementiaComputerButtons.ViewModels;

public sealed record SelfTestResult(string Item, string Status, string Detail);

public sealed class PanelButtonStatus(string name) : INotifyPropertyChanged
{
    private string _state = "UP";
    public string Name { get; } = name;
    public string State { get => _state; set { if (_state == value) return; _state = value; PropertyChanged?.Invoke(this, new(nameof(State))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PanelLedControl
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required ICommand OnCommand { get; init; }
    public required ICommand OffCommand { get; init; }
}

public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly IArduinoService _arduino;
    private readonly IVolumeService _volume;
    private readonly ISystemStateService _state;
    private readonly IButtonActionService _actions;
    private readonly IConfigurationService _configuration;
    private readonly IAudioRoutingService _audioRouting;
    private readonly IApplicationManagerService _applicationManager;
    private readonly ICallService _calls;
    private readonly IStartupService _startup;
    private readonly IUpdateService _updates;
    private readonly IFirmwareUpdateService _firmwareUpdate;
    private readonly DisplayScheduleService _displaySchedule;
    private readonly PanelLightingService _panelLighting;
    private readonly IUserPromptService _prompts;
    private readonly ILoggingService _logging;
    private TaskCompletionSource<string>? _buttonWaiter;
    private string? _expectedButton;
    private SensorSnapshot? _sensors;
    private string _connectionStatus = "Starting";
    private string _port = "Not available";
    private string _firmware = "Not available";
    private string _protocol = "DCB/1";
    private string _uptime = "Not available";
    private string _heartbeat = "Not available";
    private string _lastMessage = "Not available";
    private int _communicationErrors;
    private int _ambientLight;
    private int _microphone;
    private float _volumePercent;
    private bool _muted;
    private string _systemState = "Home";
    private string _operationStatus = "Ready";
    private string _panelHardware = "Not detected";
    private string _matrixText = "OK";
    private string _youTubeUrl = string.Empty;
    private string _videoPath = string.Empty;
    private string _spotifyUrl = "https://open.spotify.com/";
    private bool _stopVideoOnHome = true;
    private string _adrianCallMethod = "None";
    private string _adrianDisplayName = "Contact 1";
    private string _adrianCallTarget = string.Empty;
    private string _yvonneCallMethod = "None";
    private string _yvonneDisplayName = "Contact 2";
    private string _yvonneCallTarget = string.Empty;
    private bool _callQuietHoursEnabled;
    private string _callQuietFrom = "22:00";
    private string _callQuietUntil = "07:00";
    private string? _selectedSpeakerDeviceId;
    private string? _selectedHeadphoneDeviceId;
    private string? _selectedSpeakerMicrophoneId;
    private string? _selectedHeadphoneMicrophoneId;
    private string _preferredBrowser = "Edge";
    private string _adrianPhotoPath = string.Empty;
    private string _yvonnePhotoPath = string.Empty;
    private bool _displayScheduleEnabled;
    private bool _showBlackoutClock = true;
    private string _blackoutClockStyle = "Digital";
    private bool _use24HourBlackoutClock;
    private string _blackoutDateFormat = "dddd, dd/MM/yyyy";
    private string _blackoutClockColor = "#B8B8B8";
    private double _blackoutClockBrightness = 65;
    private string _blackoutWakePrompt = "PRESS A BUTTON TO BEGIN";
    private string _blackoutFrom = "22:00";
    private string _resumeAt = "07:00";
    private bool _startWithWindows;
    private string _updateStatus = "Not checked";
    private bool _canInstallUpdate;
    private UpdateRelease? _availableUpdate;
    private string _firmwareUpdateStatus = "Not checked";
    private double _panelBrightness = 100;
    private double _longPressMilliseconds = 1500;

    public DiagnosticsViewModel(IArduinoService arduino, IVolumeService volume, ISystemStateService state, IButtonActionService actions,
        IConfigurationService configuration,
        IAudioRoutingService audioRouting,
        IApplicationManagerService applicationManager,
        ICallService calls,
        IStartupService startup,
        IUpdateService updates,
        IFirmwareUpdateService firmwareUpdate,
        DisplayScheduleService displaySchedule,
        PanelLightingService panelLighting,
        IUserPromptService prompts, ILoggingService logging)
    {
        _arduino = arduino;
        _volume = volume;
        _state = state;
        _actions = actions;
        _configuration = configuration;
        _audioRouting = audioRouting;
        _applicationManager = applicationManager;
        _calls = calls;
        _startup = startup;
        _updates = updates;
        _firmwareUpdate = firmwareUpdate;
        _displaySchedule = displaySchedule;
        _panelLighting = panelLighting;
        _prompts = prompts;
        _logging = logging;
        SetRgb1Command = new AsyncRelayCommand(() => SendAsync($"RGB 0 {(byte)Rgb1Red} {(byte)Rgb1Green} {(byte)Rgb1Blue}"));
        SetRgb2Command = new AsyncRelayCommand(() => SendAsync($"RGB 1 {(byte)Rgb2Red} {(byte)Rgb2Green} {(byte)Rgb2Blue}"));
        MatrixClearCommand = new AsyncRelayCommand(() => SendAsync("MATRIX CLEAR"));
        MatrixPatternCommand = new AsyncRelayCommand(() => SendAsync("MATRIX PATTERN"));
        MatrixTextCommand = new AsyncRelayCommand(() => SendAsync($"MATRIX TEXT {SanitizeMatrixText(MatrixText)}"));
        MatrixOkCommand = new AsyncRelayCommand(() => SendAsync("MATRIX ICON OK"));
        MatrixXCommand = new AsyncRelayCommand(() => SendAsync("MATRIX ICON X"));
        BuzzerCommand = new AsyncRelayCommand(() => SendAsync("BUZZ 1000 50"));
        VolumeDownCommand = new RelayCommand(_ => _volume.ChangeBy(-5));
        VolumeUpCommand = new RelayCommand(_ => _volume.ChangeBy(5));
        MuteCommand = new RelayCommand(_ => _volume.ToggleMute());
        RunSelfTestCommand = new AsyncRelayCommand(RunSelfTestAsync);
        PanelAllOffCommand = new AsyncRelayCommand(() => SendAsync("PANEL ALL OFF"));
        SetPanelBrightnessCommand = new AsyncRelayCommand(SetPanelBrightnessAsync);
        ConsoleActionCommand = new AsyncParameterCommand(SimulatePanelButtonAsync);
        BrowseVideoCommand = new RelayCommand(_ => BrowseForVideo());
        BrowseAdrianPhotoCommand = new RelayCommand(_ => AdrianPhotoPath = BrowseForPhoto(AdrianPhotoPath) ?? AdrianPhotoPath);
        BrowseYvonnePhotoCommand = new RelayCommand(_ => YvonnePhotoPath = BrowseForPhoto(YvonnePhotoPath) ?? YvonnePhotoPath);
        SaveContentCommand = new AsyncRelayCommand(SaveContentAsync);
        ExportSettingsCommand = new AsyncRelayCommand(ExportSettingsAsync);
        ImportSettingsCommand = new AsyncRelayCommand(ImportSettingsAsync);
        ChooseBlackoutClockColorCommand = new RelayCommand(_ => ChooseBlackoutClockColor());
        RefreshAudioDevicesCommand = new RelayCommand(_ => RefreshAudioDevices());
        RefreshApplicationsCommand = new RelayCommand(_ => RefreshApplications());
        InstallApplicationCommand = new AsyncParameterCommand(InstallApplicationAsync);
        ConfigureMicroSipCommand = new RelayCommand(_ => ConfigureMicroSip());
        OpenActivityLogCommand = new RelayCommand(_ => OpenActivityLogFolder());
        CheckForUpdatesCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(false));
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync);
        UpdateControllerFirmwareCommand = new AsyncRelayCommand(UpdateControllerFirmwareAsync);

        var buttonNames = new[] { "Volume Down", "Volume Up", NormalizeName(configuration.Current.Calls.Adrian.DisplayName, "Contact 1"),
            NormalizeName(configuration.Current.Calls.Yvonne.DisplayName, "Contact 2"), "TV", "Video", "Stop/Home", "Speakers" };
        foreach (var name in buttonNames) PanelButtons.Add(new(name));
        var ledNames = new[] { "Volume Down", "Volume Up", "TV", "Video", "Stop/Home", "Speakers" };
        for (var index = 0; index < ledNames.Length; ++index)
        {
            var captured = index;
            PanelLeds.Add(new PanelLedControl
            {
                Index = index,
                Name = ledNames[index],
                OnCommand = new AsyncRelayCommand(() => SendAsync($"PANEL LED {captured} ON")),
                OffCommand = new AsyncRelayCommand(() => SendAsync($"PANEL LED {captured} OFF"))
            });
        }
        YouTubeUrl = configuration.Current.Content.YouTubeUrl;
        VideoPath = configuration.Current.Content.VideoPath;
        SpotifyUrl = configuration.Current.Content.SpotifyUrl;
        PreferredBrowser = configuration.Current.Content.PreferredBrowser;
        StopVideoOnHome = configuration.Current.Content.StopVideoOnHome;
        AdrianDisplayName = NormalizeName(configuration.Current.Calls.Adrian.DisplayName, "Contact 1");
        AdrianCallMethod = configuration.Current.Calls.Adrian.Method;
        AdrianCallTarget = configuration.Current.Calls.Adrian.Target;
        AdrianPhotoPath = configuration.Current.Calls.Adrian.PhotoPath;
        YvonneDisplayName = NormalizeName(configuration.Current.Calls.Yvonne.DisplayName, "Contact 2");
        YvonneCallMethod = configuration.Current.Calls.Yvonne.Method;
        YvonneCallTarget = configuration.Current.Calls.Yvonne.Target;
        YvonnePhotoPath = configuration.Current.Calls.Yvonne.PhotoPath;
        CallQuietHoursEnabled = configuration.Current.Calls.QuietHours.Enabled;
        CallQuietFrom = configuration.Current.Calls.QuietHours.From;
        CallQuietUntil = configuration.Current.Calls.QuietHours.Until;
        DisplayScheduleEnabled = configuration.Current.DisplaySchedule.Enabled;
        ShowBlackoutClock = configuration.Current.DisplaySchedule.ShowClock;
        BlackoutClockStyle = configuration.Current.DisplaySchedule.ClockStyle;
        Use24HourBlackoutClock = configuration.Current.DisplaySchedule.Use24HourClock;
        BlackoutDateFormat = configuration.Current.DisplaySchedule.DateFormat;
        BlackoutClockColor = configuration.Current.DisplaySchedule.ClockColor;
        BlackoutClockBrightness = configuration.Current.DisplaySchedule.ClockBrightnessPercent;
        BlackoutWakePrompt = configuration.Current.DisplaySchedule.WakePromptText;
        BlackoutFrom = configuration.Current.DisplaySchedule.BlackoutFrom;
        ResumeAt = configuration.Current.DisplaySchedule.ResumeAt;
        StartWithWindows = configuration.Current.Startup.StartWithWindows || startup.IsEnabled;
        RefreshAudioDevices();
        SelectedSpeakerDeviceId = ResolveSavedDeviceId(configuration.Current.AudioRouting.SpeakerDeviceId,
            configuration.Current.AudioRouting.SpeakerDeviceName);
        SelectedHeadphoneDeviceId = ResolveSavedDeviceId(configuration.Current.AudioRouting.HeadphoneDeviceId,
            configuration.Current.AudioRouting.HeadphoneDeviceName);
        SelectedSpeakerMicrophoneId = ResolveSavedInputDeviceId(configuration.Current.AudioRouting.SpeakerMicrophoneId,
            configuration.Current.AudioRouting.SpeakerMicrophoneName);
        SelectedHeadphoneMicrophoneId = ResolveSavedInputDeviceId(configuration.Current.AudioRouting.HeadphoneMicrophoneId,
            configuration.Current.AudioRouting.HeadphoneMicrophoneName);
        PanelBrightness = configuration.Current.Lighting.PanelBrightnessPercent;
        LongPressMilliseconds = configuration.Current.Controller.LongPressMilliseconds;
        RefreshApplications();
        UpdateStatus = $"Version {_updates.CurrentVersion.ToString(3)}";
        RefreshFirmwareUpdateStatus();
        if (configuration.Current.Updates.CheckAutomatically) _ = CheckForUpdatesAsync(true);

        _arduino.ConnectionChanged += (_, snapshot) => OnUi(() => { ApplyConnection(snapshot); RefreshFirmwareUpdateStatus(); });
        _arduino.SensorsChanged += (_, snapshot) => OnUi(() => ApplySensors(snapshot));
        _arduino.ButtonChanged += (_, button) => OnUi(() => ApplyButton(button));
        _arduino.MessageReceived += (_, message) => OnUi(() => LastMessage = message);
        _volume.VolumeChanged += (_, percent) => OnUi(() => { VolumePercent = percent; Muted = _volume.IsMuted; });
        _state.StateChanged += (_, value) => OnUi(() => SystemState = DescribeState(value));
        _panelLighting.LightStatesChanged += (_, _) => OnUi(RaisePreviewLightProperties);
        ApplyConnection(_arduino.Connection);
        VolumePercent = _volume.CurrentVolumePercent;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<SelfTestResult> SelfTestResults { get; } = [];
    public ObservableCollection<PanelButtonStatus> PanelButtons { get; } = [];
    public ObservableCollection<PanelLedControl> PanelLeds { get; } = [];
    public ObservableCollection<AudioOutputDevice> AudioOutputDevices { get; } = [];
    public ObservableCollection<AudioOutputDevice> AudioInputDevices { get; } = [];
    public ObservableCollection<ManagedApplication> Applications { get; } = [];
    public ICommand SetRgb1Command { get; }
    public ICommand SetRgb2Command { get; }
    public ICommand MatrixClearCommand { get; }
    public ICommand MatrixPatternCommand { get; }
    public ICommand MatrixTextCommand { get; }
    public ICommand MatrixOkCommand { get; }
    public ICommand MatrixXCommand { get; }
    public ICommand BuzzerCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand RunSelfTestCommand { get; }
    public ICommand PanelAllOffCommand { get; }
    public ICommand SetPanelBrightnessCommand { get; }
    public ICommand ConsoleActionCommand { get; }
    public ICommand BrowseVideoCommand { get; }
    public ICommand BrowseAdrianPhotoCommand { get; }
    public ICommand BrowseYvonnePhotoCommand { get; }
    public ICommand SaveContentCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }
    public ICommand ChooseBlackoutClockColorCommand { get; }
    public ICommand RefreshAudioDevicesCommand { get; }
    public ICommand RefreshApplicationsCommand { get; }
    public ICommand InstallApplicationCommand { get; }
    public ICommand ConfigureMicroSipCommand { get; }
    public ICommand OpenActivityLogCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand UpdateControllerFirmwareCommand { get; }

    public string ConnectionStatus { get => _connectionStatus; private set => Set(ref _connectionStatus, value); }
    public string Port { get => _port; private set => Set(ref _port, value); }
    public string Firmware { get => _firmware; private set => Set(ref _firmware, value); }
    public string Protocol { get => _protocol; private set => Set(ref _protocol, value); }
    public string Uptime { get => _uptime; private set => Set(ref _uptime, value); }
    public string Heartbeat { get => _heartbeat; private set => Set(ref _heartbeat, value); }
    public string LastMessage { get => _lastMessage; private set => Set(ref _lastMessage, value); }
    public int CommunicationErrors { get => _communicationErrors; private set => Set(ref _communicationErrors, value); }
    public int AmbientLight { get => _ambientLight; private set => Set(ref _ambientLight, value); }
    public int Microphone { get => _microphone; private set => Set(ref _microphone, value); }
    public float VolumePercent { get => _volumePercent; private set => Set(ref _volumePercent, value); }
    public bool Muted { get => _muted; private set => Set(ref _muted, value); }
    public string SystemState { get => _systemState; private set => Set(ref _systemState, value); }
    public string OperationStatus { get => _operationStatus; private set => Set(ref _operationStatus, value); }
    public string PanelHardware { get => _panelHardware; private set => Set(ref _panelHardware, value); }
    public string MatrixText { get => _matrixText; set => Set(ref _matrixText, value); }
    public string YouTubeUrl { get => _youTubeUrl; set => Set(ref _youTubeUrl, value); }
    public string VideoPath { get => _videoPath; set => Set(ref _videoPath, value); }
    public string SpotifyUrl { get => _spotifyUrl; set => Set(ref _spotifyUrl, value); }
    public string PreferredBrowser { get => _preferredBrowser; set => Set(ref _preferredBrowser, value); }
    public bool StopVideoOnHome { get => _stopVideoOnHome; set => Set(ref _stopVideoOnHome, value); }
    public string AdrianCallMethod { get => _adrianCallMethod; set => Set(ref _adrianCallMethod, value); }
    public string AdrianDisplayName { get => _adrianDisplayName; set => Set(ref _adrianDisplayName, value); }
    public string AdrianCallTarget { get => _adrianCallTarget; set => Set(ref _adrianCallTarget, value); }
    public string AdrianPhotoPath { get => _adrianPhotoPath; set => Set(ref _adrianPhotoPath, value); }
    public string YvonneCallMethod { get => _yvonneCallMethod; set => Set(ref _yvonneCallMethod, value); }
    public string YvonneDisplayName { get => _yvonneDisplayName; set => Set(ref _yvonneDisplayName, value); }
    public string YvonneCallTarget { get => _yvonneCallTarget; set => Set(ref _yvonneCallTarget, value); }
    public string YvonnePhotoPath { get => _yvonnePhotoPath; set => Set(ref _yvonnePhotoPath, value); }
    public bool CallQuietHoursEnabled { get => _callQuietHoursEnabled; set => Set(ref _callQuietHoursEnabled, value); }
    public string CallQuietFrom { get => _callQuietFrom; set => Set(ref _callQuietFrom, value); }
    public string CallQuietUntil { get => _callQuietUntil; set => Set(ref _callQuietUntil, value); }
    public bool DisplayScheduleEnabled { get => _displayScheduleEnabled; set => Set(ref _displayScheduleEnabled, value); }
    public bool ShowBlackoutClock { get => _showBlackoutClock; set => Set(ref _showBlackoutClock, value); }
    public string BlackoutClockStyle { get => _blackoutClockStyle; set => Set(ref _blackoutClockStyle, value); }
    public bool Use24HourBlackoutClock { get => _use24HourBlackoutClock; set => Set(ref _use24HourBlackoutClock, value); }
    public string BlackoutDateFormat { get => _blackoutDateFormat; set => Set(ref _blackoutDateFormat, value); }
    public string BlackoutClockColor { get => _blackoutClockColor; set => Set(ref _blackoutClockColor, value); }
    public double BlackoutClockBrightness { get => _blackoutClockBrightness; set => Set(ref _blackoutClockBrightness, value); }
    public string BlackoutWakePrompt { get => _blackoutWakePrompt; set => Set(ref _blackoutWakePrompt, value); }
    public string BlackoutFrom { get => _blackoutFrom; set => Set(ref _blackoutFrom, value); }
    public string ResumeAt { get => _resumeAt; set => Set(ref _resumeAt, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }
    public bool CanInstallUpdate { get => _canInstallUpdate; private set => Set(ref _canInstallUpdate, value); }
    public string FirmwareUpdateStatus { get => _firmwareUpdateStatus; private set => Set(ref _firmwareUpdateStatus, value); }
    public string? SelectedSpeakerDeviceId { get => _selectedSpeakerDeviceId; set => Set(ref _selectedSpeakerDeviceId, value); }
    public string? SelectedHeadphoneDeviceId { get => _selectedHeadphoneDeviceId; set => Set(ref _selectedHeadphoneDeviceId, value); }
    public string? SelectedSpeakerMicrophoneId { get => _selectedSpeakerMicrophoneId; set => Set(ref _selectedSpeakerMicrophoneId, value); }
    public string? SelectedHeadphoneMicrophoneId { get => _selectedHeadphoneMicrophoneId; set => Set(ref _selectedHeadphoneMicrophoneId, value); }
    public double Rgb1Red { get; set; }
    public double Rgb1Green { get; set; } = 180;
    public double Rgb1Blue { get; set; }
    public double Rgb2Red { get; set; }
    public double Rgb2Green { get; set; } = 180;
    public double Rgb2Blue { get; set; }
    public double PanelBrightness { get => _panelBrightness; set => Set(ref _panelBrightness, value); }
    public double LongPressMilliseconds { get => _longPressMilliseconds; set => Set(ref _longPressMilliseconds, value); }
    public bool VolumeDownLightOn => PreviewLight(0);
    public bool VolumeUpLightOn => PreviewLight(1);
    public bool TvLightOn => PreviewLight(2);
    public bool VideoLightOn => PreviewLight(3);
    public bool StopLightOn => PreviewLight(4);
    public bool HeadphonesLightOn => PreviewLight(5);

    private void OpenActivityLogFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DementiaComputerButtons", "logs");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            if (!silent) UpdateStatus = "Checking GitHub for updates...";
            _availableUpdate = await _updates.CheckAsync();
            CanInstallUpdate = _availableUpdate is not null;
            UpdateStatus = _availableUpdate is null
                ? $"Version {_updates.CurrentVersion.ToString(3)} is up to date"
                : $"Version {_availableUpdate.Version.ToString(3)} is available";
        }
        catch (Exception exception)
        {
            CanInstallUpdate = false;
            UpdateStatus = "Update check unavailable";
            _logging.Warning("update_check_failed", exception.Message);
        }
    }

    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is null) return;
        try
        {
            UpdateStatus = $"Downloading version {_availableUpdate.Version.ToString(3)}...";
            await _updates.DownloadAndInstallAsync(_availableUpdate);
            UpdateStatus = "Update verified; follow Windows Installer";
        }
        catch (Exception exception)
        {
            UpdateStatus = $"UPDATE FAILED: {exception.Message}";
            _logging.Error("update_install_failed", exception, exception.Message);
        }
    }

    private void RefreshFirmwareUpdateStatus()
    {
        var current = _arduino.Connection;
        FirmwareUpdateStatus = !_firmwareUpdate.IsToolAvailable
            ? "Arduino CLI is not installed"
            : !current.IsConnected
                ? $"Connect the controller to install bundled firmware {_firmwareUpdate.BundledVersion}"
                : $"Controller {current.FirmwareVersion ?? "unknown"}; bundled {_firmwareUpdate.BundledVersion}";
    }

    private async Task UpdateControllerFirmwareAsync()
    {
        if (!_prompts.Confirm(
                $"Flash firmware {_firmwareUpdate.BundledVersion} to the connected KS0501 controller? Do not unplug it until verification finishes.",
                "Update controller firmware")) return;
        try
        {
            OperationStatus = $"Updating controller firmware to {_firmwareUpdate.BundledVersion}...";
            await _firmwareUpdate.UpdateControllerAsync();
            RefreshFirmwareUpdateStatus();
            OperationStatus = $"Controller firmware {_firmwareUpdate.BundledVersion} installed and verified";
        }
        catch (Exception exception)
        {
            RefreshFirmwareUpdateStatus();
            OperationStatus = $"FIRMWARE UPDATE FAILED: {exception.Message}";
            _logging.Error("firmware_update_failed", exception, exception.Message);
        }
    }

    private async Task SimulatePanelButtonAsync(object? parameter)
    {
        if (parameter is not string button) return;
        await _actions.HandleAsync(new ButtonEvent(button, "DOWN", 0));
        await _actions.HandleAsync(new ButtonEvent(button, "UP", 0));
    }

    private void BrowseForVideo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the video for the Video button",
            Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v;*.webm|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true) VideoPath = dialog.FileName;
    }

    private static string? BrowseForPhoto(string current)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a contact photo",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*",
            CheckFileExists = true,
            FileName = current
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void ChooseBlackoutClockColor()
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try { dialog.Color = System.Drawing.ColorTranslator.FromHtml(BlackoutClockColor); }
        catch { dialog.Color = System.Drawing.Color.FromArgb(184, 184, 184); }
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        BlackoutClockColor = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private async Task SaveContentAsync()
    {
        try
        {
            _configuration.Current.Content.YouTubeUrl = YouTubeUrl.Trim();
            _configuration.Current.Content.VideoPath = VideoPath.Trim();
            _configuration.Current.Content.SpotifyUrl = SpotifyUrl.Trim();
            _configuration.Current.Content.PreferredBrowser = PreferredBrowser;
            _configuration.Current.Content.StopVideoOnHome = StopVideoOnHome;
            _configuration.Current.Calls.Adrian.DisplayName = NormalizeName(AdrianDisplayName, "Contact 1");
            _configuration.Current.Calls.Adrian.Method = AdrianCallMethod;
            _configuration.Current.Calls.Adrian.Target = AdrianCallTarget.Trim();
            _configuration.Current.Calls.Adrian.PhotoPath = AdrianPhotoPath.Trim();
            _configuration.Current.Calls.Yvonne.DisplayName = NormalizeName(YvonneDisplayName, "Contact 2");
            _configuration.Current.Calls.Yvonne.Method = YvonneCallMethod;
            _configuration.Current.Calls.Yvonne.Target = YvonneCallTarget.Trim();
            _configuration.Current.Calls.Yvonne.PhotoPath = YvonnePhotoPath.Trim();
            if (!TimeOnly.TryParseExact(CallQuietFrom, "HH:mm", out _) || !TimeOnly.TryParseExact(CallQuietUntil, "HH:mm", out _))
                throw new InvalidOperationException("Call quiet hours must use 24-hour HH:mm format, for example 22:00 and 07:00.");
            _configuration.Current.Calls.QuietHours.Enabled = CallQuietHoursEnabled;
            _configuration.Current.Calls.QuietHours.From = CallQuietFrom;
            _configuration.Current.Calls.QuietHours.Until = CallQuietUntil;
            if (!TimeOnly.TryParseExact(BlackoutFrom, "HH:mm", out _) || !TimeOnly.TryParseExact(ResumeAt, "HH:mm", out _))
                throw new InvalidOperationException("Display times must use 24-hour HH:mm format, for example 22:00 and 07:00.");
            _configuration.Current.DisplaySchedule.Enabled = DisplayScheduleEnabled;
            _configuration.Current.DisplaySchedule.ShowClock = ShowBlackoutClock;
            _configuration.Current.DisplaySchedule.ClockStyle = BlackoutClockStyle;
            _configuration.Current.DisplaySchedule.Use24HourClock = Use24HourBlackoutClock;
            _configuration.Current.DisplaySchedule.DateFormat = BlackoutDateFormat.Trim();
            _configuration.Current.DisplaySchedule.ClockColor = BlackoutClockColor;
            _configuration.Current.DisplaySchedule.ClockBrightnessPercent = (byte)Math.Clamp(Math.Round(BlackoutClockBrightness), 1, 100);
            _configuration.Current.DisplaySchedule.WakePromptText = BlackoutWakePrompt.Trim();
            _configuration.Current.DisplaySchedule.BlackoutFrom = BlackoutFrom;
            _configuration.Current.DisplaySchedule.ResumeAt = ResumeAt;
            _configuration.Current.Startup.StartWithWindows = StartWithWindows;
            _configuration.Current.Controller.LongPressMilliseconds = (int)Math.Clamp(Math.Round(LongPressMilliseconds), 500, 10000);
            _startup.SetEnabled(StartWithWindows);
            SaveAudioDeviceSelection();
            await _configuration.SaveAsync();
            _displaySchedule.Evaluate();
            if (_arduino.Connection.IsConnected && Version.TryParse(_arduino.Connection.FirmwareVersion, out var firmwareVersion) &&
                firmwareVersion >= new Version(0, 2, 2))
            {
                await _arduino.SendCommandAsync($"PANEL LONGPRESS {_configuration.Current.Controller.LongPressMilliseconds}");
                OperationStatus = "Console settings and controller long-press time saved";
            }
            else OperationStatus = _arduino.Connection.IsConnected
                ? "Settings saved; update controller firmware to apply the long-press time"
                : "Content and call settings saved";
        }
        catch (Exception exception)
        {
            OperationStatus = $"SAVE FAILED: {exception.Message}";
            _logging.Error("content_settings_save_failed", exception, exception.Message);
        }
    }

    private async Task ExportSettingsAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Dementia Computer Buttons settings",
            Filter = "JSON settings|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"DementiaComputerButtons-settings-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() != true) return;

        await SaveContentAsync();
        if (OperationStatus.StartsWith("SAVE FAILED", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            await _configuration.ExportAsync(dialog.FileName);
            OperationStatus = $"Settings exported to {dialog.FileName}";
            _logging.Information("settings_exported", dialog.FileName);
        }
        catch (Exception exception)
        {
            OperationStatus = $"EXPORT FAILED: {exception.Message}";
            _logging.Error("settings_export_failed", exception, dialog.FileName);
        }
    }

    private async Task ImportSettingsAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Dementia Computer Buttons settings",
            Filter = "JSON settings|*.json|All files|*.*",
            DefaultExt = ".json",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        if (!_prompts.Confirm(
                "Replace this computer's Dementia Computer Buttons settings with the selected file? Media and photo files are not copied.",
                "Import settings")) return;

        try
        {
            await _configuration.ImportAsync(dialog.FileName);
            LoadEditorFromConfiguration();
            _startup.SetEnabled(StartWithWindows);
            _displaySchedule.Evaluate();
            OperationStatus = $"Settings imported from {dialog.FileName}";
            _logging.Information("settings_imported", dialog.FileName);
        }
        catch (Exception exception)
        {
            OperationStatus = $"IMPORT FAILED: {exception.Message}";
            _logging.Error("settings_import_failed", exception, dialog.FileName);
        }
    }

    private void LoadEditorFromConfiguration()
    {
        var current = _configuration.Current;
        YouTubeUrl = current.Content.YouTubeUrl;
        VideoPath = current.Content.VideoPath;
        SpotifyUrl = current.Content.SpotifyUrl;
        PreferredBrowser = current.Content.PreferredBrowser;
        StopVideoOnHome = current.Content.StopVideoOnHome;
        AdrianDisplayName = NormalizeName(current.Calls.Adrian.DisplayName, "Contact 1");
        AdrianCallMethod = current.Calls.Adrian.Method;
        AdrianCallTarget = current.Calls.Adrian.Target;
        AdrianPhotoPath = current.Calls.Adrian.PhotoPath;
        YvonneDisplayName = NormalizeName(current.Calls.Yvonne.DisplayName, "Contact 2");
        YvonneCallMethod = current.Calls.Yvonne.Method;
        YvonneCallTarget = current.Calls.Yvonne.Target;
        YvonnePhotoPath = current.Calls.Yvonne.PhotoPath;
        CallQuietHoursEnabled = current.Calls.QuietHours.Enabled;
        CallQuietFrom = current.Calls.QuietHours.From;
        CallQuietUntil = current.Calls.QuietHours.Until;
        DisplayScheduleEnabled = current.DisplaySchedule.Enabled;
        ShowBlackoutClock = current.DisplaySchedule.ShowClock;
        BlackoutClockStyle = current.DisplaySchedule.ClockStyle;
        Use24HourBlackoutClock = current.DisplaySchedule.Use24HourClock;
        BlackoutDateFormat = current.DisplaySchedule.DateFormat;
        BlackoutClockColor = current.DisplaySchedule.ClockColor;
        BlackoutClockBrightness = current.DisplaySchedule.ClockBrightnessPercent;
        BlackoutWakePrompt = current.DisplaySchedule.WakePromptText;
        BlackoutFrom = current.DisplaySchedule.BlackoutFrom;
        ResumeAt = current.DisplaySchedule.ResumeAt;
        StartWithWindows = current.Startup.StartWithWindows;
        PanelBrightness = current.Lighting.PanelBrightnessPercent;
        LongPressMilliseconds = current.Controller.LongPressMilliseconds;
        RefreshAudioDevices();
        SelectedSpeakerDeviceId = ResolveSavedDeviceId(current.AudioRouting.SpeakerDeviceId, current.AudioRouting.SpeakerDeviceName);
        SelectedHeadphoneDeviceId = ResolveSavedDeviceId(current.AudioRouting.HeadphoneDeviceId, current.AudioRouting.HeadphoneDeviceName);
        SelectedSpeakerMicrophoneId = ResolveSavedInputDeviceId(current.AudioRouting.SpeakerMicrophoneId, current.AudioRouting.SpeakerMicrophoneName);
        SelectedHeadphoneMicrophoneId = ResolveSavedInputDeviceId(current.AudioRouting.HeadphoneMicrophoneId, current.AudioRouting.HeadphoneMicrophoneName);
    }

    private void RefreshAudioDevices()
    {
        AudioOutputDevices.Clear();
        foreach (var device in _audioRouting.GetOutputDevices()) AudioOutputDevices.Add(device);
        AudioInputDevices.Clear();
        foreach (var device in _audioRouting.GetInputDevices()) AudioInputDevices.Add(device);
    }

    private void RefreshApplications()
    {
        Applications.Clear();
        foreach (var application in _applicationManager.Scan()) Applications.Add(application);
    }

    private async Task InstallApplicationAsync(object? parameter)
    {
        if (parameter is not string key) return;
        var application = Applications.FirstOrDefault(item => item.Key == key);
        if (application is null || application.IsInstalled) return;
        try
        {
            OperationStatus = $"Installing {application.Name}…";
            var success = await _applicationManager.InstallAsync(key);
            RefreshApplications();
            OperationStatus = success ? $"{application.Name} installed" : $"INSTALL FAILED: {application.Name}";
        }
        catch (Exception exception)
        {
            OperationStatus = $"INSTALL FAILED: {exception.Message}";
            _logging.Error("application_install_failed", exception, application.Name);
        }
    }

    private void ConfigureMicroSip()
    {
        try
        {
            var applicationPath = Environment.ProcessPath ?? throw new InvalidOperationException("Dad Console executable path was not available.");
            OperationStatus = _calls.ConfigureMicroSipIntegration(applicationPath);
        }
        catch (Exception exception)
        {
            OperationStatus = $"MICROSIP SETUP FAILED: {exception.Message}";
            _logging.Error("microsip_integration_failed", exception, exception.Message);
        }
    }

    private string? ResolveSavedDeviceId(string id, string name) =>
        AudioOutputDevices.FirstOrDefault(device => device.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Id ??
        AudioOutputDevices.FirstOrDefault(device => device.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))?.Id;

    private string? ResolveSavedInputDeviceId(string id, string name) =>
        AudioInputDevices.FirstOrDefault(device => device.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Id ??
        AudioInputDevices.FirstOrDefault(device => device.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))?.Id;

    private void SaveAudioDeviceSelection()
    {
        var speakers = AudioOutputDevices.FirstOrDefault(device => device.Id == SelectedSpeakerDeviceId);
        var headphones = AudioOutputDevices.FirstOrDefault(device => device.Id == SelectedHeadphoneDeviceId);
        var speakerMicrophone = AudioInputDevices.FirstOrDefault(device => device.Id == SelectedSpeakerMicrophoneId);
        var headphoneMicrophone = AudioInputDevices.FirstOrDefault(device => device.Id == SelectedHeadphoneMicrophoneId);
        _configuration.Current.AudioRouting.SpeakerDeviceId = speakers?.Id ?? string.Empty;
        _configuration.Current.AudioRouting.SpeakerDeviceName = speakers?.Name ?? string.Empty;
        _configuration.Current.AudioRouting.HeadphoneDeviceId = headphones?.Id ?? string.Empty;
        _configuration.Current.AudioRouting.HeadphoneDeviceName = headphones?.Name ?? string.Empty;
        _configuration.Current.AudioRouting.SpeakerMicrophoneId = speakerMicrophone?.Id ?? string.Empty;
        _configuration.Current.AudioRouting.SpeakerMicrophoneName = speakerMicrophone?.Name ?? string.Empty;
        _configuration.Current.AudioRouting.HeadphoneMicrophoneId = headphoneMicrophone?.Id ?? string.Empty;
        _configuration.Current.AudioRouting.HeadphoneMicrophoneName = headphoneMicrophone?.Name ?? string.Empty;
    }

    private async Task SetPanelBrightnessAsync()
    {
        var percent = (byte)Math.Clamp(Math.Round(PanelBrightness), 0, 100);
        _configuration.Current.Lighting.PanelBrightnessPercent = percent;
        await _configuration.SaveAsync();
        await SendAsync($"PANEL BRIGHTNESS {percent}");
    }
    private async Task SendAsync(string command)
    {
        try { await _arduino.SendCommandAsync(command); OperationStatus = $"ACK: {command}"; }
        catch (Exception exception) { OperationStatus = $"FAILED: {exception.Message}"; _logging.Error("diagnostic_command_failed", exception, command); }
    }

    private async Task RunSelfTestAsync()
    {
        SelfTestResults.Clear();
        if (!_arduino.Connection.IsConnected)
        { SelfTestResults.Add(new("Controller", "FAIL", "Controller is not connected.")); return; }

        await VerifyVisibleAsync("Red LED / Video output", "PANEL LED 3 ON", "PANEL LED 3 OFF", "Did the red onboard LED illuminate?");
        await VerifyVisibleAsync("Yellow LED / Stop output", "PANEL LED 4 ON", "PANEL LED 4 ON", "Is the yellow onboard LED illuminated?");
        await VerifyVisibleAsync("Green LED / Speakers output", "PANEL LED 5 ON", "PANEL LED 5 OFF", "Did the green onboard LED illuminate?");
        await VerifyVisibleAsync("RGB LED 1", "RGB 0 255 0 255", "RGB 0 0 180 0", "Did RGB LED 1 turn magenta?");
        await VerifyVisibleAsync("RGB LED 2", "RGB 1 0 255 255", "RGB 1 0 180 0", "Did RGB LED 2 turn cyan?");
        await VerifyVisibleAsync("Matrix", "MATRIX PATTERN", "MATRIX ICON OK", "Did the matrix show a checkerboard pattern?");
        await VerifyAudibleAsync();
        await VerifyButtonAsync("PANEL_1", "left/volume-down");
        await VerifyButtonAsync("PANEL_2", "right/volume-up");
        AddSensorResult("Ambient light", _sensors?.AmbientLight);
        AddSensorResult("Microphone", _sensors?.Microphone);
        OperationStatus = "Hardware self-test finished";
    }

    private async Task VerifyVisibleAsync(string item, string on, string restore, string question)
    {
        try
        {
            await _arduino.SendCommandAsync(on);
            var observed = _prompts.Confirm(question, $"Self-test: {item}");
            await _arduino.SendCommandAsync(restore);
            SelfTestResults.Add(new(item, observed ? "PASS" : "FAIL", observed ? "Observed by operator" : "Not observed by operator"));
        }
        catch (Exception exception) { SelfTestResults.Add(new(item, "FAIL", exception.Message)); }
    }

    private async Task VerifyAudibleAsync()
    {
        try
        {
            await _arduino.SendCommandAsync("BUZZ 1000 50");
            var heard = _prompts.Confirm("Did you hear a short beep?", "Self-test: buzzer");
            SelfTestResults.Add(new("Buzzer", heard ? "PASS" : "FAIL", heard ? "Observed by operator" : "Not heard"));
        }
        catch (Exception exception) { SelfTestResults.Add(new("Buzzer", "FAIL", exception.Message)); }
    }

    private async Task VerifyButtonAsync(string button, string label)
    {
        try
        {
            _expectedButton = button;
            _buttonWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _prompts.Inform($"Press the onboard {label} button now.", $"Self-test: {label} button");
            await _buttonWaiter.Task.WaitAsync(TimeSpan.FromSeconds(15));
            SelfTestResults.Add(new($"{label} button", "PASS", "DOWN event received"));
        }
        catch (TimeoutException) { SelfTestResults.Add(new($"{label} button", "FAIL", "No event within 15 seconds; check D2/D3 DIP switches")); }
        finally { _expectedButton = null; _buttonWaiter = null; }
    }

    private void AddSensorResult(string name, int? value) => SelfTestResults.Add(value.HasValue
        ? new(name, "PASS", $"Raw value {value.Value} received")
        : new(name, "FAIL", "No telemetry received"));

    private void ApplyConnection(ArduinoConnectionSnapshot value)
    {
        ConnectionStatus = value.IsConnected ? value.StatusDetail ?? "Connected" : value.StatusDetail ?? "Disconnected";
        Port = value.PortName ?? "Not available";
        Firmware = value.FirmwareVersion ?? "Not available";
        Protocol = value.ProtocolVersion;
        Uptime = value.UptimeMilliseconds > 0 ? TimeSpan.FromMilliseconds(value.UptimeMilliseconds).ToString(@"d\.hh\:mm\:ss") : "Not available";
        Heartbeat = value.LastHeartbeatUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "Not available";
        CommunicationErrors = value.CommunicationErrors;
        PanelHardware = $"Direct buttons: {(value.PanelButtonsAvailable ? "configured" : "unavailable")}; direct LEDs: {(value.PanelLedsAvailable ? "configured" : "unavailable")}";
        if (value.LastMessage is not null) LastMessage = value.LastMessage;
    }

    private void ApplySensors(SensorSnapshot value)
    { _sensors = value; AmbientLight = value.AmbientLight; Microphone = value.Microphone; }

    private void ApplyButton(ButtonEvent value)
    {
        if (value.Button.StartsWith("PANEL_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value.Button.AsSpan(6), out var number) && number >= 1 && number <= PanelButtons.Count)
            PanelButtons[number - 1].State = value.Action;
        if (value.Action == "DOWN" && value.Button.Equals(_expectedButton, StringComparison.OrdinalIgnoreCase))
            _buttonWaiter?.TrySetResult(value.Button);
    }

    private static string SanitizeMatrixText(string text)
    {
        var clean = new string(text.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
        return string.IsNullOrEmpty(clean) ? "OK" : clean;
    }

    private string DescribeState(DadConsoleState value) => value switch
    {
        DadConsoleState.CallingAdrian => $"Calling {NormalizeName(AdrianDisplayName, "Contact 1")}",
        DadConsoleState.CallingMum => $"Calling {NormalizeName(YvonneDisplayName, "Contact 2")}",
        DadConsoleState.ListeningSpotify => "Spotify",
        DadConsoleState.IncomingCall => "Incoming call",
        DadConsoleState.InCall => "In call",
        _ => value.ToString()
    };

    private static string NormalizeName(string value, string fallback)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }

    private bool PreviewLight(int index) => index < _panelLighting.CurrentLightStates.Count &&
        _panelLighting.CurrentLightStates[index];

    private void RaisePreviewLightProperties()
    {
        foreach (var name in new[] { nameof(VolumeDownLightOn), nameof(VolumeUpLightOn), nameof(TvLightOn),
                     nameof(VideoLightOn), nameof(StopLightOn), nameof(HeadphonesLightOn) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class AsyncParameterCommand(Func<object?, Task> execute) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running;
    public async void Execute(object? parameter)
    {
        if (_running) return;
        _running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(parameter); }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
