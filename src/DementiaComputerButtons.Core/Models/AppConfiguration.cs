namespace DementiaComputerButtons.Core.Models;

public sealed class AppConfiguration
{
    public int VolumeStepPercent { get; set; } = 5;
    public int VolumeHoldStepPercent { get; set; } = 2;
    public ControllerConfiguration Controller { get; set; } = new();
    public Dictionary<string, string> ButtonMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LEFT"] = "VOLUME_DOWN",
        ["RIGHT"] = "VOLUME_UP",
        ["PANEL_1"] = "VOLUME_DOWN",
        ["PANEL_2"] = "VOLUME_UP",
        ["PANEL_3"] = "CALL_ADRIAN",
        ["PANEL_4"] = "CALL_YVONNE",
        ["PANEL_5"] = "WATCH_TV",
        ["PANEL_6"] = "PLAY_VIDEO",
        ["PANEL_7"] = "STOP",
        ["PANEL_8"] = "SPEAKERS_TOGGLE"
    };
    public Dictionary<string, string> LongPressButtonMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PANEL_5"] = "OPEN_SPOTIFY",
        ["PANEL_7"] = "BLACKOUT"
    };
    public ContentConfiguration Content { get; set; } = new();
    public CallsConfiguration Calls { get; set; } = new();
    public AudioRoutingConfiguration AudioRouting { get; set; } = new();
    public LightingConfiguration Lighting { get; set; } = new();
    public DisplayScheduleConfiguration DisplaySchedule { get; set; } = new();
    public StartupConfiguration Startup { get; set; } = new();
    public UpdateConfiguration Updates { get; set; } = new();
    public Dictionary<string, string> IrCommands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UpdateConfiguration
{
    public bool CheckAutomatically { get; set; } = true;
    public string GitHubOwner { get; set; } = "themusicnerd";
    public string GitHubRepository { get; set; } = "Dementia-Computer-Buttons";
}

public sealed class StartupConfiguration
{
    public bool StartWithWindows { get; set; }
}

public sealed class DisplayScheduleConfiguration
{
    public bool Enabled { get; set; }
    public string BlackoutFrom { get; set; } = "22:00";
    public string ResumeAt { get; set; } = "07:00";
}

public sealed class AudioRoutingConfiguration
{
    public string SpeakerDeviceId { get; set; } = string.Empty;
    public string SpeakerDeviceName { get; set; } = string.Empty;
    public string HeadphoneDeviceId { get; set; } = string.Empty;
    public string HeadphoneDeviceName { get; set; } = string.Empty;
    public string SpeakerMicrophoneId { get; set; } = string.Empty;
    public string SpeakerMicrophoneName { get; set; } = string.Empty;
    public string HeadphoneMicrophoneId { get; set; } = string.Empty;
    public string HeadphoneMicrophoneName { get; set; } = string.Empty;
}

public sealed class CallsConfiguration
{
    public string MicroSipPath { get; set; } = string.Empty;
    public CallQuietHoursConfiguration QuietHours { get; set; } = new();
    public ContactCallConfiguration Adrian { get; set; } = new() { DisplayName = "Contact 1" };
    public ContactCallConfiguration Yvonne { get; set; } = new() { DisplayName = "Contact 2" };
}

public sealed class CallQuietHoursConfiguration
{
    public bool Enabled { get; set; }
    public string From { get; set; } = "22:00";
    public string Until { get; set; } = "07:00";
}

public sealed class ContactCallConfiguration
{
    public string DisplayName { get; set; } = string.Empty;
    public string Method { get; set; } = "None";
    public string Target { get; set; } = string.Empty;
    public string PhotoPath { get; set; } = string.Empty;
}

public sealed class ControllerConfiguration
{
    public int BaudRate { get; set; } = 115200;
    public string Protocol { get; set; } = "DCB/1";
    public string ExpectedBoard { get; set; } = "KS0501";
    public int ReconnectDelayMs { get; set; } = 1000;
    public int HeartbeatIntervalMs { get; set; } = 1000;
    public int HeartbeatTimeoutMs { get; set; } = 4000;
}

public sealed class ContentConfiguration
{
    public string YouTubeUrl { get; set; } = string.Empty;
    public string VideoPath { get; set; } = string.Empty;
    public string PreferredBrowserPath { get; set; } = string.Empty;
    public string PreferredBrowser { get; set; } = "Edge";
    public string VlcPath { get; set; } = string.Empty;
    public bool StopVideoOnHome { get; set; } = true;
    public string SpotifyUrl { get; set; } = "https://open.spotify.com/";
    public string VideoDisplayName { get; set; } = "Orientation Video";
}

public sealed class LightingConfiguration
{
    public byte NormalBrightness { get; set; } = 64;
    public byte NightBrightness { get; set; } = 16;
    public byte PanelBrightnessPercent { get; set; } = 100;
}
