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
            Validate(Current);
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
        Validate(Current);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await WriteConfigurationAsync(temporary, Current, cancellationToken);
        File.Move(temporary, path, true);
        logging.Information("configuration_saved", path);
    }

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        Validate(Current);
        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = fullDestination + ".tmp";
        await WriteConfigurationAsync(temporary, Current, cancellationToken);
        File.Move(temporary, fullDestination, true);
        logging.Information("configuration_exported", fullDestination);
    }

    public async Task ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullSource = Path.GetFullPath(sourcePath);
        AppConfiguration imported;
        try
        {
            await using var stream = File.OpenRead(fullSource);
            imported = await JsonSerializer.DeserializeAsync<AppConfiguration>(stream, JsonOptions, cancellationToken)
                ?? throw new JsonException("The settings file did not contain a configuration object.");
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            logging.Error("configuration_import_failed", exception, fullSource);
            throw new InvalidDataException($"The selected settings file is not valid: {exception.Message}", exception);
        }

        _warnings.Clear();
        Validate(imported);
        Current = imported;
        await SaveAsync(cancellationToken);
        logging.Information("configuration_imported", fullSource);
    }

    private static async Task WriteConfigurationAsync(string destination, AppConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
    }

    private void Validate(AppConfiguration configuration)
    {
        configuration.Controller ??= new ControllerConfiguration();
        configuration.ButtonMappings = configuration.ButtonMappings is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(configuration.ButtonMappings, StringComparer.OrdinalIgnoreCase);
        configuration.LongPressButtonMappings = configuration.LongPressButtonMappings is null
            ? new(StringComparer.OrdinalIgnoreCase)
            : new(configuration.LongPressButtonMappings, StringComparer.OrdinalIgnoreCase);
        configuration.LongPressButtonMappings.TryAdd("PANEL_7", "BLACKOUT");
        configuration.Content ??= new ContentConfiguration();
        configuration.Calls ??= new CallsConfiguration();
        configuration.Calls.QuietHours ??= new CallQuietHoursConfiguration();
        configuration.Calls.Adrian ??= new ContactCallConfiguration { DisplayName = "Contact 1" };
        configuration.Calls.Yvonne ??= new ContactCallConfiguration { DisplayName = "Contact 2" };
        configuration.AudioRouting ??= new AudioRoutingConfiguration();
        configuration.Lighting ??= new LightingConfiguration();
        configuration.DisplaySchedule ??= new DisplayScheduleConfiguration();
        configuration.Startup ??= new StartupConfiguration();
        configuration.ApplianceMode ??= new ApplianceModeConfiguration();
        configuration.Updates ??= new UpdateConfiguration();
        configuration.IrCommands ??= new(StringComparer.OrdinalIgnoreCase);
        if (configuration.VolumeStepPercent is < 1 or > 25)
        {
            _warnings.Add("VolumeStepPercent must be 1..25; using 5.");
            configuration.VolumeStepPercent = 5;
        }
        if (configuration.VolumeHoldStepPercent is < 1 or > 10)
        {
            _warnings.Add("VolumeHoldStepPercent must be 1..10; using 2.");
            configuration.VolumeHoldStepPercent = 2;
        }
        if (configuration.Controller.BaudRate != 115200)
        {
            _warnings.Add("Only 115200 baud is supported; using 115200.");
            configuration.Controller.BaudRate = 115200;
        }
        if (!string.Equals(configuration.Controller.Protocol, "DCB/1", StringComparison.Ordinal))
        {
            _warnings.Add("Unsupported controller protocol; using DCB/1.");
            configuration.Controller.Protocol = "DCB/1";
        }
        if (configuration.Controller.LongPressMilliseconds is < 500 or > 10000)
        {
            _warnings.Add("Long-press time must be 500..10000 ms; using 1500.");
            configuration.Controller.LongPressMilliseconds = 1500;
        }
        if (string.IsNullOrWhiteSpace(configuration.Calls.Adrian.DisplayName)) configuration.Calls.Adrian.DisplayName = "Contact 1";
        if (string.IsNullOrWhiteSpace(configuration.Calls.Yvonne.DisplayName)) configuration.Calls.Yvonne.DisplayName = "Contact 2";
        if (!TimeOnly.TryParseExact(configuration.Calls.QuietHours.From, "HH:mm", out _))
        {
            _warnings.Add("Call quiet-hours start is invalid; using 22:00.");
            configuration.Calls.QuietHours.From = "22:00";
        }
        if (!TimeOnly.TryParseExact(configuration.Calls.QuietHours.Until, "HH:mm", out _))
        {
            _warnings.Add("Call quiet-hours end is invalid; using 07:00.");
            configuration.Calls.QuietHours.Until = "07:00";
        }
        if (!TimeOnly.TryParseExact(configuration.DisplaySchedule.BlackoutFrom, "HH:mm", out _))
        {
            _warnings.Add("Display blackout time is invalid; using 22:00.");
            configuration.DisplaySchedule.BlackoutFrom = "22:00";
        }
        if (!TimeOnly.TryParseExact(configuration.DisplaySchedule.ResumeAt, "HH:mm", out _))
        {
            _warnings.Add("Display resume time is invalid; using 07:00.");
            configuration.DisplaySchedule.ResumeAt = "07:00";
        }
        if (configuration.DisplaySchedule.ClockStyle is not ("Digital" or "Analogue"))
        {
            _warnings.Add("Blackout clock style is invalid; using Digital.");
            configuration.DisplaySchedule.ClockStyle = "Digital";
        }
        try { _ = DateTime.Now.ToString(configuration.DisplaySchedule.DateFormat); }
        catch (FormatException)
        {
            _warnings.Add("Blackout date format is invalid; using Australian day/date format.");
            configuration.DisplaySchedule.DateFormat = "dddd, dd/MM/yyyy";
        }
        var colour = configuration.DisplaySchedule.ClockColor;
        if (colour is null || colour.Length != 7 || colour[0] != '#' ||
            !int.TryParse(colour.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            _warnings.Add("Blackout clock colour is invalid; using soft white.");
            configuration.DisplaySchedule.ClockColor = "#B8B8B8";
        }
        if (configuration.DisplaySchedule.ClockBrightnessPercent is < 1 or > 100)
        {
            _warnings.Add("Blackout clock brightness must be 1..100; using 65.");
            configuration.DisplaySchedule.ClockBrightnessPercent = 65;
        }
        configuration.DisplaySchedule.WakePromptText ??= string.Empty;
        if (configuration.DisplaySchedule.WakePromptText.Length > 80)
        {
            _warnings.Add("Blackout instruction text was shortened to 80 characters.");
            configuration.DisplaySchedule.WakePromptText = configuration.DisplaySchedule.WakePromptText[..80];
        }
    }
}
