using System.Text.Json;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Core.Services;

public sealed class ConfigurationService(string path, ILoggingService logging) : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly List<string> _warnings = [];
    public AppConfiguration Current { get; private set; } = new();
    public IReadOnlyList<string> Warnings => _warnings;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _warnings.Clear();
        if (!File.Exists(path))
        {
            _warnings.Add($"Configuration file not found: {path}; safe defaults are active.");
            logging.Warning("configuration_missing", _warnings[0]);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, JsonOptions, cancellationToken);
            Current = loaded ?? new AppConfiguration();
            Validate();
            logging.Information("configuration_loaded", path);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Current = new AppConfiguration();
            _warnings.Add($"Invalid configuration; safe defaults are active: {exception.Message}");
            logging.Error("configuration_invalid", exception, _warnings[0]);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken);
        File.Move(temporary, path, true);
        logging.Information("configuration_saved", path);
    }

    private void Validate()
    {
        Current.Calls ??= new CallsConfiguration();
        Current.Calls.QuietHours ??= new CallQuietHoursConfiguration();
        Current.Startup ??= new StartupConfiguration();
        Current.Updates ??= new UpdateConfiguration();
        if (Current.VolumeStepPercent is < 1 or > 25)
        {
            _warnings.Add("VolumeStepPercent must be 1..25; using 5.");
            Current.VolumeStepPercent = 5;
        }
        if (Current.VolumeHoldStepPercent is < 1 or > 10)
        {
            _warnings.Add("VolumeHoldStepPercent must be 1..10; using 2.");
            Current.VolumeHoldStepPercent = 2;
        }
        if (Current.Controller.BaudRate != 115200)
        {
            _warnings.Add("Only 115200 baud is supported; using 115200.");
            Current.Controller.BaudRate = 115200;
        }
        if (!Current.Controller.Protocol.Equals("DCB/1", StringComparison.Ordinal))
        {
            _warnings.Add("Unsupported controller protocol; using DCB/1.");
            Current.Controller.Protocol = "DCB/1";
        }
        if (string.IsNullOrWhiteSpace(Current.Calls.Adrian.DisplayName)) Current.Calls.Adrian.DisplayName = "Contact 1";
        if (string.IsNullOrWhiteSpace(Current.Calls.Yvonne.DisplayName)) Current.Calls.Yvonne.DisplayName = "Contact 2";
        if (!TimeOnly.TryParseExact(Current.Calls.QuietHours.From, "HH:mm", out _))
        {
            _warnings.Add("Call quiet-hours start is invalid; using 22:00.");
            Current.Calls.QuietHours.From = "22:00";
        }
        if (!TimeOnly.TryParseExact(Current.Calls.QuietHours.Until, "HH:mm", out _))
        {
            _warnings.Add("Call quiet-hours end is invalid; using 07:00.");
            Current.Calls.QuietHours.Until = "07:00";
        }
        if (!TimeOnly.TryParseExact(Current.DisplaySchedule.BlackoutFrom, "HH:mm", out _))
        {
            _warnings.Add("Display blackout time is invalid; using 22:00.");
            Current.DisplaySchedule.BlackoutFrom = "22:00";
        }
        if (!TimeOnly.TryParseExact(Current.DisplaySchedule.ResumeAt, "HH:mm", out _))
        {
            _warnings.Add("Display resume time is invalid; using 07:00.");
            Current.DisplaySchedule.ResumeAt = "07:00";
        }
    }
}
