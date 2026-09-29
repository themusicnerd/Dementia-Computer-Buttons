using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DementiaComputerButtons.Core.Abstractions;
using Microsoft.Win32;

namespace DementiaComputerButtons.Infrastructure;

public sealed class WindowsApplianceModeService(ILoggingService logging) : IApplianceModeService, IDisposable
{
    private const string ExplorerPolicyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private const string SystemPolicyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string AutoLogonPackage = "Microsoft.Sysinternals.Autologon";
    private const string AutoLogonHelp = "https://learn.microsoft.com/sysinternals/downloads/autologon";
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;
    private const uint DisplayRequired = 0x00000002;

    public bool IsSessionProtectionEnabled => ReadPolicy(ExplorerPolicyPath, "NoLogoff") &&
        ReadPolicy(SystemPolicyPath, "DisableLockWorkstation");

    public void SetSessionProtection(bool enabled)
    {
        SetPolicy(ExplorerPolicyPath, "NoLogoff", enabled);
        SetPolicy(SystemPolicyPath, "DisableLockWorkstation", enabled);
        if (SetThreadExecutionState(enabled ? Continuous | SystemRequired | DisplayRequired : Continuous) == 0)
            throw new InvalidOperationException("Windows could not update the sleep-prevention state.");
        logging.Information("appliance_session_protection", enabled
            ? "Sign out and lock controls hidden; automatic sleep prevented while the controller runs."
            : "Normal sign out, lock and power behaviour restored.");
    }

    public async Task<string> ConfigureAutoLogonAsync(CancellationToken cancellationToken = default)
    {
        var executable = FindAutoLogon();
        if (executable is null)
        {
            var winget = FindWinget();
            if (winget is null)
            {
                OpenHelp();
                return "Opened Microsoft Autologon download instructions; Windows Package Manager is unavailable.";
            }

            var start = new ProcessStartInfo(winget)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "install", "--id", AutoLogonPackage, "--exact", "--source", "winget", "--silent",
                         "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start winget.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Microsoft Autologon installation failed: {ShortText(error, output)}");
            executable = FindAutoLogon();
        }

        if (executable is null)
        {
            OpenHelp();
            return "Autologon was installed but could not be located; Microsoft setup instructions were opened.";
        }

        _ = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" })
            ?? throw new InvalidOperationException("Windows could not start Microsoft Autologon.");
        logging.Information("autologon_setup_opened", executable);
        return "Microsoft Autologon opened. Confirm the account, enter its password (or leave it blank), then select Enable.";
    }

    public void Dispose() => _ = SetThreadExecutionState(Continuous);

    private static bool ReadPolicy(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) is int value && value == 1;
    }

    private static void SetPolicy(string path, string name, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, true);
        if (enabled) key.SetValue(name, 1, RegistryValueKind.DWord);
        else key.DeleteValue(name, false);
    }

    private static string? FindAutoLogon()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(local, "Microsoft", "WinGet", "Links", "Autologon64.exe"),
            Path.Combine(local, "Microsoft", "WinGet", "Links", "Autologon.exe"),
            Path.Combine(programFiles, "Sysinternals Suite", "Autologon64.exe"),
            Path.Combine(programFiles, "Sysinternals Suite", "Autologon.exe")
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null) return found;
        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(packages)) return null;
        try
        {
            return Directory.EnumerateDirectories(packages, $"{AutoLogonPackage}_*")
                .SelectMany(path => Directory.EnumerateFiles(path, "Autologon64.exe", SearchOption.AllDirectories))
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? FindWinget()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(path) ? path : null;
    }

    private static void OpenHelp() => _ = Process.Start(new ProcessStartInfo(AutoLogonHelp) { UseShellExecute = true });

    private static string ShortText(params string[] values)
    {
        var text = string.Join(" ", values).Trim();
        return text.Length > 300 ? text[..300] : text;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint executionState);
}
