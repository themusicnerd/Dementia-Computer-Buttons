using System.Runtime.InteropServices;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Infrastructure;

public sealed class WindowsVolumeService : IVolumeService, IDisposable
{
    private readonly ILoggingService _logging;
    private readonly object _sync = new();
    private readonly System.Threading.Timer _pollTimer;
    private float _current;
    private bool _muted;
    private bool _initialized;
    private DateTimeOffset _lastErrorLogUtc = DateTimeOffset.MinValue;

    public WindowsVolumeService(ILoggingService logging)
    {
        _logging = logging;
        _pollTimer = new System.Threading.Timer(_ => Refresh(), null, 500, 500);
    }
    public event EventHandler<float>? VolumeChanged;
    public float CurrentVolumePercent { get { lock (_sync) return _current; } }
    public bool IsMuted { get { lock (_sync) return _muted; } }

    public void Refresh() => WithEndpoint(endpoint =>
    {
        Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var scalar));
        Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
        var current = Math.Clamp(scalar * 100f, 0, 100);
        bool changed;
        lock (_sync)
        {
            changed = !_initialized || Math.Abs(current - _current) >= 0.5f || muted != _muted;
            _current = current;
            _muted = muted;
            _initialized = true;
        }
        if (changed) VolumeChanged?.Invoke(this, current);
    });

    public void ChangeBy(float deltaPercent) => SetVolume(_current + deltaPercent);
    public void SetVolume(float percent) => WithEndpoint(endpoint =>
    {
        var clamped = Math.Clamp(percent, 0, 100);
        Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(clamped / 100f, Guid.Empty));
        lock (_sync) { _current = clamped; _initialized = true; }
        VolumeChanged?.Invoke(this, clamped);
        _logging.Information("volume_changed", $"percent={clamped:0}");
    });
    public void ToggleMute() => WithEndpoint(endpoint =>
    {
        bool muted;
        float current;
        lock (_sync) { muted = !_muted; current = _current; }
        Marshal.ThrowExceptionForHR(endpoint.SetMute(muted, Guid.Empty));
        lock (_sync) _muted = muted;
        _logging.Information("mute_changed", $"muted={muted}");
        VolumeChanged?.Invoke(this, current);
    });

    private void WithEndpoint(Action<IAudioEndpointVolume> action)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            var enumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!;
            enumerator = (IMMDeviceEnumerator)(Activator.CreateInstance(enumeratorType)
                ?? throw new InvalidOperationException("Windows Core Audio enumerator was unavailable."));
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 0, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var instance));
            endpoint = (IAudioEndpointVolume)instance;
            action(endpoint);
        }
        catch (Exception exception)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _lastErrorLogUtc >= TimeSpan.FromSeconds(30))
            {
                _lastErrorLogUtc = now;
                _logging.Error("windows_volume_error", exception, exception.Message);
            }
        }
        finally
        {
            if (endpoint is not null) Marshal.ReleaseComObject(endpoint);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    public void Dispose() => _pollTimer.Dispose();

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int classContext, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
