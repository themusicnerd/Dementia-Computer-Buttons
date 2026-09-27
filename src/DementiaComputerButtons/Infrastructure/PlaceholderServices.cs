using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Infrastructure;

public sealed class VlcService(IConfigurationService configuration, ILoggingService logging) : IVlcService
{
    private readonly object _sync = new();
    private Process? _managedProcess;
    public bool IsPlaying
    {
        get
        {
            lock (_sync)
                return _managedProcess is { HasExited: false };
        }
    }
    public event EventHandler? PlaybackEnded;

    public async Task PlayAsync(string path, CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        var executable = FindVlc(configuration.Current.Content.VlcPath)
            ?? throw new InvalidOperationException("VLC is not installed and content.vlcPath is not valid.");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("The configured local video file does not exist.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add("--fullscreen");
        start.ArgumentList.Add("--video-on-top");
        start.ArgumentList.Add("--play-and-exit");
        start.ArgumentList.Add("--no-repeat");
        start.ArgumentList.Add("--no-loop");
        start.ArgumentList.Add("--no-video-title-show");
        start.ArgumentList.Add(path);
        var process = new Process { StartInfo = start };
        process.Exited += OnProcessExited;
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Windows did not start VLC.");
        }
        lock (_sync) _managedProcess = process;
        // Enabling this after storing the process also reports an exit that happened
        // immediately after Start, without racing the ownership check in OnProcessExited.
        process.EnableRaisingEvents = true;
        await WindowActivation.BringToFrontAsync(process, cancellationToken).ConfigureAwait(false);
        logging.Information("vlc_started", path);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_sync)
        {
            process = _managedProcess;
            _managedProcess = null;
        }
        await ManagedProcess.StopAsync(process, cancellationToken).ConfigureAwait(false);
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        if (sender is not Process process) return;
        lock (_sync)
        {
            if (!ReferenceEquals(_managedProcess, process)) return;
            _managedProcess = null;
        }
        logging.Information("vlc_finished", "The selected video reached the end and VLC exited.");
        logging.Information("activity_media", "Video finished");
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
        process.Dispose();
    }

    private static string? FindVlc(string configured) => Existing(configured,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VideoLAN", "VLC", "vlc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VideoLAN", "VLC", "vlc.exe"));

    private static string? Existing(params string[] paths) => paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
}

public sealed class BrowserService(IConfigurationService configuration, ILoggingService logging) : IBrowserService
{
    private Process? _managedProcess;

    public async Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("content.youTubeUrl must be a valid HTTP or HTTPS address.");
        var executable = FindBrowser(configuration.Current.Content.PreferredBrowserPath,
                configuration.Current.Content.PreferredBrowser)
            ?? throw new InvalidOperationException("No supported browser installation was found.");
        await CloseManagedContentAsync(cancellationToken).ConfigureAwait(false);
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DementiaComputerButtons", "browser-profile");
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add($"--user-data-dir={profile}");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--autoplay-policy=no-user-gesture-required");
        start.ArgumentList.Add("--kiosk");
        if (Path.GetFileName(executable).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add("--edge-kiosk-type=fullscreen");
        start.ArgumentList.Add(uri.AbsoluteUri);
        _managedProcess = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start the browser.");
        await WindowActivation.BringToFrontAsync(_managedProcess, cancellationToken).ConfigureAwait(false);
        logging.Information("browser_started", uri.Host);
    }

    public async Task CloseManagedContentAsync(CancellationToken cancellationToken = default)
    {
        var process = _managedProcess;
        _managedProcess = null;
        await ManagedProcess.StopAsync(process, cancellationToken).ConfigureAwait(false);
    }

    private static string? FindBrowser(string configured, string preferred)
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var edge = new[] { Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe") };
        var chrome = new[] { Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe") };
        return new[] { configured }
            .Concat(preferred.Equals("Chrome", StringComparison.OrdinalIgnoreCase) ? chrome.Concat(edge) : edge.Concat(chrome))
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

}

public sealed class CallService(IConfigurationService configuration, ILoggingService logging) : ICallService
{
    private string? _activeMicroSipPath;
    public CallSnapshot Current { get; private set; } = new(CallStatus.Idle);
    public bool IsActive => Current.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or
        CallStatus.Connecting or CallStatus.Connected;
    public bool IsCallingBlocked
    {
        get
        {
            var quiet = configuration.Current.Calls.QuietHours;
            return DailyTimeRange.Contains(quiet.Enabled, quiet.From, quiet.Until, TimeOnly.FromDateTime(DateTime.Now));
        }
    }
    public event EventHandler<CallSnapshot>? StatusChanged;

    public Task StartCallAsync(string contactKey, CancellationToken cancellationToken = default)
    {
        if (IsCallingBlocked)
            throw new InvalidOperationException("Calling is unavailable during the configured quiet hours.");
        var contact = contactKey.ToUpperInvariant() switch
        {
            "ADRIAN" => configuration.Current.Calls.Adrian,
            "YVONNE" => configuration.Current.Calls.Yvonne,
            _ => throw new InvalidOperationException($"Unknown call contact: {contactKey}")
        };
        var method = contact.Method.Trim();
        if (method.Equals("None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(method))
            throw new InvalidOperationException($"No call method is configured for {contactKey}.");
        if (!method.Equals("SIP", StringComparison.OrdinalIgnoreCase) && !method.Equals("Zoom", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported call method '{contact.Method}'.");

        var targetText = contact.Target.Trim();
        if (string.IsNullOrWhiteSpace(targetText) || targetText.Length > 256 || targetText.Any(char.IsControl))
            throw new InvalidOperationException($"The configured call target for {contactKey} is invalid.");
        Uri? target = null;
        if (method.Equals("Zoom", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(targetText, UriKind.Absolute, out target) || target.Scheme is not ("https" or "zoommtg"))
                throw new InvalidOperationException($"The Zoom target for {contactKey} must be an HTTPS or zoommtg link.");
        }
        else if (targetText.Contains(':') &&
                 (!Uri.TryCreate(targetText, UriKind.Absolute, out target) || target.Scheme is not ("sip" or "sips" or "tel")))
            throw new InvalidOperationException($"The SIP target for {contactKey} is invalid.");

        if (method.Equals("SIP", StringComparison.OrdinalIgnoreCase) && FindMicroSip() is { } microSip)
        {
            var start = new ProcessStartInfo(microSip) { UseShellExecute = false };
            start.ArgumentList.Add(targetText);
            _ = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start MicroSIP.");
            _activeMicroSipPath = microSip;
        }
        else
        {
            var registeredTarget = method.Equals("SIP", StringComparison.OrdinalIgnoreCase) && target is null
                ? $"sip:{targetText}" : target!.AbsoluteUri;
            _ = Process.Start(new ProcessStartInfo(registeredTarget) { UseShellExecute = true })
                ?? throw new InvalidOperationException("Windows has no application registered for the configured call link.");
            _activeMicroSipPath = null;
        }
        logging.Information("call_started", $"contact={contactKey}; method={method}");
        Publish(new(CallStatus.Calling, DisplayName(contact, contactKey), contact.Target, contact.PhotoPath,
            method, DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task AnswerIncomingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var microSip = FindMicroSip() ?? throw new InvalidOperationException("MicroSIP is not installed.");
        RunMicroSipCommand(microSip, "/answer");
        _activeMicroSipPath = microSip;
        Publish(Current with { Status = CallStatus.Connecting });
        logging.Information("incoming_call_answered", Current.CallerId);
        return Task.CompletedTask;
    }

    public async Task RejectIncomingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var microSip = FindMicroSip() ?? throw new InvalidOperationException("MicroSIP is not installed.");
        RunMicroSipCommand(microSip, "/hangupincoming");
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        MinimizeMicroSipWindows();
        Publish(Current with { Status = CallStatus.Ended });
        logging.Information("incoming_call_rejected", Current.CallerId);
    }

    public async Task EndCallAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive) return;
        if (_activeMicroSipPath is { } microSip && File.Exists(microSip))
        {
            RunMicroSipCommand(microSip, "/hangupall");
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            MinimizeMicroSipWindows();
            logging.Information("sip_call_ended", "MicroSIP /hangupall requested and its window minimized.");
            _activeMicroSipPath = null;
        }
        else
        {
            logging.Information("call_end_requested", "Call ownership remains with the registered Zoom/SIP client.");
        }
        Publish(Current with { Status = CallStatus.Ended });
    }

    public void HandleExternalEvent(string eventName, string callerId)
    {
        if (IsCallingBlocked)
        {
            Current = new(CallStatus.Idle);
            if (eventName.Equals("INCOMING", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (FindMicroSip() is { } microSip)
                        RunMicroSipCommand(microSip, "/hangupincoming");
                    _ = MinimizeMicroSipAfterDelayAsync();
                    logging.Information("call_blocked_quiet_hours", $"Incoming call declined; caller={callerId}");
                    logging.Information("activity_call", $"Incoming call blocked during quiet hours ({callerId})");
                }
                catch (Exception exception)
                {
                    logging.Error("call_block_quiet_hours_failed", exception, callerId);
                }
            }
            return;
        }
        var contact = ResolveCaller(callerId);
        var snapshot = eventName.ToUpperInvariant() switch
        {
            "INCOMING" => new CallSnapshot(CallStatus.Incoming, DisplayName(contact, callerId), callerId,
                contact?.PhotoPath ?? string.Empty, "SIP", DateTimeOffset.UtcNow),
            "RINGING" => Current with { Status = CallStatus.Ringing, CallerId = ValueOr(callerId, Current.CallerId), Method = ValueOr(Current.Method, "SIP") },
            "CONNECTED" => Current with { Status = CallStatus.Connected, DisplayName = DisplayName(contact, Current.DisplayName),
                CallerId = ValueOr(callerId, Current.CallerId), PhotoPath = contact?.PhotoPath ?? Current.PhotoPath,
                Method = ValueOr(Current.Method, "SIP"), StartedAtUtc = Current.StartedAtUtc ?? DateTimeOffset.UtcNow,
                ConnectedAtUtc = Current.ConnectedAtUtc ?? DateTimeOffset.UtcNow },
            "ENDED" => Current with { Status = CallStatus.Ended, DisplayName = DisplayName(contact, Current.DisplayName),
                CallerId = ValueOr(callerId, Current.CallerId), PhotoPath = contact?.PhotoPath ?? Current.PhotoPath },
            _ => Current
        };
        if (snapshot.Status == CallStatus.Ended) _activeMicroSipPath = null;
        if (snapshot != Current) Publish(snapshot);
        logging.Information("external_call_event", $"event={eventName}; caller={callerId}");
    }

    public string ConfigureMicroSipIntegration(string applicationPath)
    {
        var microSip = FindMicroSip() ?? throw new InvalidOperationException("Install MicroSIP before configuring incoming calls.");
        if (!File.Exists(applicationPath)) throw new InvalidOperationException("Dementia Computer Buttons executable path is invalid.");
        var runningMicroSip = Process.GetProcessesByName("MicroSIP");
        if (runningMicroSip.Length > 0)
        {
            RunMicroSipCommand(microSip, "/exit");
            foreach (var process in runningMicroSip)
            {
                try { process.WaitForExit(5000); }
                finally { process.Dispose(); }
            }
        }
        var hookDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DementiaComputerButtons", "MicroSipHooks");
        Directory.CreateDirectory(hookDirectory);
        var hooks = new Dictionary<string, string>
        {
            ["cmdIncomingCall"] = CreateHook("incoming", "INCOMING"),
            ["cmdCallRing"] = CreateHook("ringing", "RINGING"),
            ["cmdCallStart"] = CreateHook("connected", "CONNECTED"),
            ["cmdCallAnswer"] = CreateHook("answered", "CONNECTED"),
            ["cmdCallEnd"] = CreateHook("ended", "ENDED")
        };
        var roamingIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicroSIP", "microsip.ini");
        var portableIni = Path.Combine(Path.GetDirectoryName(microSip)!, "microsip.ini");
        var ini = File.Exists(roamingIni) || !File.Exists(portableIni) ? roamingIni : portableIni;
        Directory.CreateDirectory(Path.GetDirectoryName(ini)!);
        foreach (var hook in hooks)
            if (!WritePrivateProfileString("Settings", hook.Key, hook.Value, ini))
                throw new InvalidOperationException($"Windows could not update {ini}.");
        var restart = new ProcessStartInfo(microSip) { UseShellExecute = false };
        restart.ArgumentList.Add("/minimized");
        _ = Process.Start(restart) ?? throw new InvalidOperationException("MicroSIP could not be restarted.");
        logging.Information("microsip_integration_configured", ini);
        return "MicroSIP call status integration configured and restarted.";

        string CreateHook(string fileName, string eventName)
        {
            var path = Path.Combine(hookDirectory, $"dcb-{fileName}.cmd");
            File.WriteAllText(path, $"@echo off\r\n\"{applicationPath}\" --call-event {eventName} \"%~1\"\r\n");
            return path;
        }

    }

    private ContactCallConfiguration? ResolveCaller(string callerId)
    {
        var contacts = new[] { configuration.Current.Calls.Adrian, configuration.Current.Calls.Yvonne };
        return contacts.FirstOrDefault(contact => !string.IsNullOrWhiteSpace(contact.Target) &&
            (contact.Target.Contains(callerId, StringComparison.OrdinalIgnoreCase) || callerId.Contains(CallUser(contact.Target), StringComparison.OrdinalIgnoreCase)));
    }

    private static string CallUser(string target) => target.Replace("sip:", "", StringComparison.OrdinalIgnoreCase)
        .Replace("sips:", "", StringComparison.OrdinalIgnoreCase).Replace("tel:", "", StringComparison.OrdinalIgnoreCase).Split('@')[0];
    private static string DisplayName(ContactCallConfiguration? contact, string fallback) =>
        contact is not null && !string.IsNullOrWhiteSpace(contact.DisplayName) ? contact.DisplayName.Trim() : fallback;
    private static string ValueOr(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private void Publish(CallSnapshot snapshot) { Current = snapshot; StatusChanged?.Invoke(this, snapshot); }
    private static void RunMicroSipCommand(string executable, string command)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.ArgumentList.Add(command);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start MicroSIP.");
    }

    private static void MinimizeMicroSipWindows()
    {
        foreach (var process in Process.GetProcessesByName("MicroSIP"))
        {
            try
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero)
                    _ = ShowWindowAsync(process.MainWindowHandle, 6); // SW_MINIMIZE
            }
            finally { process.Dispose(); }
        }
    }

    private static async Task MinimizeMicroSipAfterDelayAsync()
    {
        await Task.Delay(300).ConfigureAwait(false);
        MinimizeMicroSipWindows();
    }

    private string? FindMicroSip()
    {
        var configured = configuration.Current.Calls.MicroSipPath;
        return new[]
        {
            configured,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "MicroSIP", "microsip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MicroSIP", "microsip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MicroSIP", "microsip.exe")
        }.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(string section, string key, string value, string filePath);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);
}

public sealed class IRService : IIRService { public bool IsHardwareAvailable => false; }

internal static class ManagedProcess
{
    public static async Task StopAsync(Process? process, CancellationToken cancellationToken)
    {
        if (process is null) return;
        try
        {
            if (process.HasExited) return;
            process.CloseMainWindow();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        finally { process.Dispose(); }
    }
}

internal static class WindowActivation
{
    private const int Restore = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    public static async Task BringToFrontAsync(Process process, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30 && !process.HasExited; ++attempt)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                ShowWindowAsync(process.MainWindowHandle, Restore);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}
