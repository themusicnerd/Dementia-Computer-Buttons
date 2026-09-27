using System.Diagnostics;
using System.Runtime.InteropServices;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Infrastructure;

public sealed class WindowsShellService : IWindowsShellService
{
    private static readonly HashSet<string> StartProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellExperienceHost"
    };

    public bool CloseStartMenuIfOpen()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            if (!StartProcesses.Contains(process.ProcessName)) return false;
            // Windows 11's SearchHost ignores a posted window message. Because the
            // foreground process was positively identified above, one guarded Escape
            // input cannot leak into VLC, the browser, or another application.
            keybd_event(0x1B, 0, 0, UIntPtr.Zero); // VK_ESCAPE key down
            keybd_event(0x1B, 0, 2, UIntPtr.Zero); // KEYEVENTF_KEYUP
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
