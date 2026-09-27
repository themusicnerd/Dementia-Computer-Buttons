using System.Runtime.InteropServices;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Infrastructure;

public sealed class WindowsAudioRoutingService(IConfigurationService configuration, ILoggingService logging) : IAudioRoutingService
{
    public bool IsAvailable
    {
        get
        {
            var devices = GetOutputDevices();
            var inputs = GetInputDevices();
            var routing = configuration.Current.AudioRouting;
            return Resolve(devices, routing.SpeakerDeviceId, routing.SpeakerDeviceName) is not null &&
                   Resolve(devices, routing.HeadphoneDeviceId, routing.HeadphoneDeviceName) is not null &&
                   Resolve(inputs, routing.SpeakerMicrophoneId, routing.SpeakerMicrophoneName) is not null &&
                   Resolve(inputs, routing.HeadphoneMicrophoneId, routing.HeadphoneMicrophoneName) is not null;
        }
    }

    public bool SpeakersMuted
    {
        get
        {
            try
            {
                var devices = GetOutputDevices();
                var headphones = Resolve(devices, configuration.Current.AudioRouting.HeadphoneDeviceId,
                    configuration.Current.AudioRouting.HeadphoneDeviceName);
                return headphones is not null && string.Equals(GetDefaultDeviceId(EDataFlow.Render), headphones.Id, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                logging.Error("audio_default_read_failed", exception, exception.Message);
                return false;
            }
        }
    }

    public event EventHandler<bool>? SpeakersMutedChanged;

    public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => EnumerateDevices(EDataFlow.Render);
    public IReadOnlyList<AudioOutputDevice> GetInputDevices() => EnumerateDevices(EDataFlow.Capture);
    public AudioLevelSnapshot GetLevels() => new(GetPeak(EDataFlow.Render), GetPeak(EDataFlow.Capture));

    private static float GetPeak(EDataFlow flow)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? meterObject = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out device));
            var iid = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out meterObject));
            var meter = (IAudioMeterInformation)meterObject;
            Marshal.ThrowExceptionForHR(meter.GetPeakValue(out var peak));
            return Math.Clamp(peak, 0f, 1f);
        }
        catch { return 0; }
        finally
        {
            if (meterObject is not null && Marshal.IsComObject(meterObject)) Marshal.ReleaseComObject(meterObject);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private IReadOnlyList<AudioOutputDevice> EnumerateDevices(EDataFlow flow)
    {
        var result = new List<AudioOutputDevice>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, DeviceStateActive, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint index = 0; index < count; ++index)
            {
                Marshal.ThrowExceptionForHR(collection.Item(index, out var device));
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    result.Add(new AudioOutputDevice(id, ReadFriendlyName(device) ?? id));
                }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        catch (Exception exception) { logging.Error("audio_device_enumeration_failed", exception, exception.Message); }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
        return result.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task RestoreHomeRoutingAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> ToggleSpeakersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var devices = GetOutputDevices();
            var routing = configuration.Current.AudioRouting;
            var inputs = GetInputDevices();
            var headphones = Resolve(devices, routing.HeadphoneDeviceId, routing.HeadphoneDeviceName);
            var speakers = Resolve(devices, routing.SpeakerDeviceId, routing.SpeakerDeviceName);
            var speakerMicrophone = Resolve(inputs, routing.SpeakerMicrophoneId, routing.SpeakerMicrophoneName);
            var headphoneMicrophone = Resolve(inputs, routing.HeadphoneMicrophoneId, routing.HeadphoneMicrophoneName);
            var target = SpeakersMuted ? speakers : headphones;
            var targetMicrophone = SpeakersMuted ? speakerMicrophone : headphoneMicrophone;
            if (target is null || targetMicrophone is null || headphones is null || speakers is null ||
                speakerMicrophone is null || headphoneMicrophone is null)
            {
                logging.Warning("audio_routing_unconfigured", "Choose both output devices and both microphones in the app.");
                return Task.FromResult(false);
            }

            var policy = (IPolicyConfig)(object)new PolicyConfigClient();
            try
            {
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                {
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(target.Id, role));
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(targetMicrophone.Id, role));
                }
            }
            finally { Marshal.ReleaseComObject(policy); }

            var headphonesSelected = target.Id.Equals(headphones.Id, StringComparison.OrdinalIgnoreCase);
            logging.Information("audio_profile_changed", $"output={target.Name}; input={targetMicrophone.Name}; headphones={headphonesSelected}");
            SpeakersMutedChanged?.Invoke(this, headphonesSelected);
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            logging.Error("audio_output_change_failed", exception, exception.Message);
            return Task.FromResult(false);
        }
    }

    private static AudioOutputDevice? Resolve(IReadOnlyList<AudioOutputDevice> devices, string id, string name) =>
        devices.FirstOrDefault(device => device.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ??
        devices.FirstOrDefault(device => device.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));

    private static string GetDefaultDeviceId(EDataFlow flow)
    {
        var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var device));
            try { Marshal.ThrowExceptionForHR(device.GetId(out var id)); return id; }
            finally { Marshal.ReleaseComObject(device); }
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out var store));
        try
        {
            var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try { return value.ValueType == 31 ? Marshal.PtrToStringUni(value.PointerValue) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private const uint DeviceStateActive = 1;
    private enum EDataFlow { Render, Capture, All }
    private enum ERole { Console, Multimedia, Communications }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumeratorComObject;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint channelCount);
        [PreserveSig] int GetChannelsPeakValues(uint channelCount, IntPtr peakValues);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId) { public Guid FormatId = formatId; public uint PropertyId = propertyId; }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort ValueType;
        [FieldOffset(8)] public IntPtr PointerValue;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, IntPtr period, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int store, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int store, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
