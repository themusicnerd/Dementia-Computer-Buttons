using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Infrastructure;

public sealed class FirmwareUpdateService(IArduinoService arduino, ILoggingService logging) : IFirmwareUpdateService
{
    private const string BundledSha256 = "5256B89FE2F499CAB809303861249A7387803F6FB6F43B30B774F68547F52209";
    public string BundledVersion => "0.2.2";
    public bool IsToolAvailable => FindArduinoCli() is not null;

    public async Task UpdateControllerAsync(CancellationToken cancellationToken = default)
    {
        var connection = arduino.Connection;
        if (!connection.IsConnected ||
            !string.Equals(connection.Board, "KS0501", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(connection.PortName))
            throw new InvalidOperationException("A handshaken KS0501 controller must be connected before firmware can be updated.");

        var cli = FindArduinoCli() ?? throw new InvalidOperationException("Arduino CLI is not installed. Install it from Required applications first.");
        var firmware = Path.Combine(AppContext.BaseDirectory, "Firmware", $"keyestudio-max-{BundledVersion}.hex");
        if (!File.Exists(firmware)) throw new FileNotFoundException("The bundled controller firmware is missing.", firmware);
        await using (var stream = File.OpenRead(firmware))
        {
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!actualHash.Equals(BundledSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The bundled controller firmware failed its SHA-256 integrity check.");
        }
        var port = connection.PortName;

        logging.Information("firmware_update_started", $"board=KS0501; port={port}; version={BundledVersion}");
        await arduino.StopAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cores = await RunAsync(cli, ["core", "list"], cancellationToken).ConfigureAwait(false);
            if (!cores.Contains("arduino:avr", StringComparison.OrdinalIgnoreCase))
                await RunAsync(cli, ["core", "install", "arduino:avr"], cancellationToken).ConfigureAwait(false);

            await RunAsync(cli,
                ["upload", "--fqbn", "arduino:avr:uno", "--port", port, "--input-file", firmware, "--verify"],
                cancellationToken).ConfigureAwait(false);
            logging.Information("firmware_upload_completed", $"port={port}; version={BundledVersion}");
        }
        finally
        {
            await arduino.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = arduino.Connection;
            if (current.IsConnected)
            {
                if (!string.Equals(current.FirmwareVersion, BundledVersion, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Controller reconnected, but reported firmware {current.FirmwareVersion ?? "unknown"} instead of {BundledVersion}.");
                logging.Information("firmware_update_verified", $"port={current.PortName}; version={current.FirmwareVersion}");
                return;
            }
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Firmware uploaded, but the controller did not reconnect within 20 seconds.");
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows could not start Arduino CLI.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = string.Join(" ", error, output).Trim();
            if (detail.Length > 600) detail = detail[..600];
            throw new InvalidOperationException($"Arduino CLI failed with exit code {process.ExitCode}: {detail}");
        }
        return output;
    }

    private static string? FindArduinoCli()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Arduino CLI", "arduino-cli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "arduino-cli.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "arduino-cli.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
