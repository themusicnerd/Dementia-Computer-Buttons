namespace DementiaComputerButtons.Core.Models;

public enum DadConsoleState
{
    Home,
    WatchingTV,
    ListeningSpotify,
    PlayingVideo,
    CallingAdrian,
    CallingMum,
    IncomingCall,
    InCall,
    Error
}

public sealed record ArduinoConnectionSnapshot(
    bool IsConnected,
    string? PortName = null,
    string? Board = null,
    string? FirmwareVersion = null,
    string ProtocolVersion = "DCB/1",
    long UptimeMilliseconds = 0,
    DateTimeOffset? LastHeartbeatUtc = null,
    string? LastMessage = null,
    int CommunicationErrors = 0,
    string? StatusDetail = null,
    bool PanelButtonsAvailable = false,
    bool PanelLedsAvailable = false);

public sealed record SensorSnapshot(int AmbientLight, int Microphone, long UptimeMilliseconds);
public sealed record AudioOutputDevice(string Id, string Name);
public sealed record AudioLevelSnapshot(float SpeakerPeak, float MicrophonePeak);
public sealed record ManagedApplication(string Key, string Name, string PackageId, bool IsInstalled, string Detail)
{
    public bool CanInstall => !IsInstalled;
}
public sealed record UpdateRelease(Version Version, string Tag, string MsiUrl, string ChecksumUrl);

public enum CallStatus { Idle, Incoming, Calling, Ringing, Connecting, Connected, Ended, Failed }
public sealed record CallSnapshot(
    CallStatus Status,
    string DisplayName = "",
    string CallerId = "",
    string PhotoPath = "",
    string Method = "",
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? ConnectedAtUtc = null);

public sealed record ButtonEvent(string Button, string Action, long UptimeMilliseconds);

public enum ControllerMessageKind
{
    Ready,
    Hello,
    Acknowledgement,
    Error,
    Pong,
    ButtonEvent,
    Telemetry,
    Data,
    Unknown,
    Malformed
}

public sealed record ControllerMessage(
    ControllerMessageKind Kind,
    string Raw,
    string? RequestId = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? Error = null,
    ButtonEvent? Button = null,
    SensorSnapshot? Sensors = null);
