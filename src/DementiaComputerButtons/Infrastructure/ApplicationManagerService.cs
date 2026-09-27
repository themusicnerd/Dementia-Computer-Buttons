using System.Diagnostics;
using System.IO;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Infrastructure;

public sealed class ApplicationManagerService(ILoggingService logging) : IApplicationManagerService
{
    private sealed record Definition(string Key, string Name, string PackageId, Func<string?> FindExecutable);

    private static readonly Definition[] Definitions =
    [
        new("edge", "Microsoft Edge", "Microsoft.Edge", FindEdge),
        new("chrome", "Google Chrome", "Google.Chrome", FindChrome),
        new("vlc", "VLC media player", "VideoLAN.VLC", FindVlc),
        new("zoom", "Zoom Workplace", "Zoom.Zoom", FindZoom),
        new("microsip", "MicroSIP", "MicroSIP.MicroSIP", FindMicroSip),
        new("spotify", "Spotify", "Spotify.Spotify", FindSpotify)
    ];

    public IReadOnlyList<ManagedApplication> Scan() => Definitions.Select(definition =>
    {
        var executable = definition.FindExecutable();
        return new ManagedApplication(definition.Key, definition.Name, definition.PackageId,
            executable is not null, executable is null ? "Not installed" : $"Installed: {executable}");
    }).ToArray();

    public async Task<bool> InstallAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (definition is null) throw new ArgumentException("Unknown application.", nameof(key));
        if (definition.FindExecutable() is not null) return true;
        var winget = FindWinget() ?? throw new InvalidOperationException("Windows Package Manager (winget) is not available.");
        var start = new ProcessStartInfo(winget)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "install", "--id", definition.PackageId, "--exact", "--source", "winget", "--silent",
                     "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity" })
            start.ArgumentList.Add(argument);
        logging.Information("application_install_started", $"name={definition.Name}; package={definition.PackageId}");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start winget.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            logging.Warning("application_install_failed", $"name={definition.Name}; exit={process.ExitCode}; {Trim(error, output)}");
            return false;
        }
        logging.Information("application_install_completed", definition.Name);
        return true;
    }

    private static string Trim(params string[] values)
    {
        var text = string.Join(" ", values).Trim();
        return text.Length > 300 ? text[..300] : text;
    }
    private static string? Existing(params string[] paths) => paths.FirstOrDefault(File.Exists);
    private static string Pf(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), .. parts]);
    private static string Pfx86(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), .. parts]);
    private static string Local(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), .. parts]);
    private static string Roaming(params string[] parts) => Path.Combine([Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), .. parts]);
    private static string? FindEdge() => Existing(Pfx86("Microsoft", "Edge", "Application", "msedge.exe"), Pf("Microsoft", "Edge", "Application", "msedge.exe"));
    private static string? FindChrome() => Existing(Pf("Google", "Chrome", "Application", "chrome.exe"), Pfx86("Google", "Chrome", "Application", "chrome.exe"), Local("Google", "Chrome", "Application", "chrome.exe"));
    private static string? FindVlc() => Existing(Pf("VideoLAN", "VLC", "vlc.exe"), Pfx86("VideoLAN", "VLC", "vlc.exe"));
    private static string? FindZoom() => Existing(Roaming("Zoom", "bin", "Zoom.exe"), Pf("Zoom", "bin", "Zoom.exe"), Pfx86("Zoom", "bin", "Zoom.exe"));
    private static string? FindMicroSip() => Existing(Pf("MicroSIP", "microsip.exe"), Pfx86("MicroSIP", "microsip.exe"), Local("MicroSIP", "microsip.exe"));
    private static string? FindSpotify() => Existing(Roaming("Spotify", "Spotify.exe"));
    private static string? FindWinget() => Existing(Local("Microsoft", "WindowsApps", "winget.exe"));
}
