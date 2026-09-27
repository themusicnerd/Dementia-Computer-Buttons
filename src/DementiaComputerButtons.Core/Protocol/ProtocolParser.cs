using System.Globalization;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Core.Protocol;

public static class ProtocolParser
{
    public const int MaximumLineLength = 95;

    public static ControllerMessage Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return Malformed(line ?? string.Empty, "EMPTY");
        if (line.Length > MaximumLineLength)
            return Malformed(line, "LINE_TOO_LONG");

        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return Malformed(line, "EMPTY");

        try
        {
            return tokens[0] switch
            {
                "DCB/1" when tokens.Length >= 2 && tokens[1] == "READY" =>
                    new(ControllerMessageKind.Ready, line, Fields: ParseFields(tokens, 2)),
                "HELLO" when tokens.Length >= 4 && tokens[1] == "DCB/1" =>
                    new(ControllerMessageKind.Hello, line, Fields: ParseHello(tokens)),
                "ACK" when tokens.Length == 2 =>
                    new(ControllerMessageKind.Acknowledgement, line, RequestId: tokens[1]),
                "ERR" when tokens.Length >= 3 =>
                    new(ControllerMessageKind.Error, line, RequestId: tokens[1], Error: string.Join(' ', tokens.Skip(2))),
                "PONG" when tokens.Length >= 2 =>
                    new(ControllerMessageKind.Pong, line, RequestId: tokens[1], Fields: ParseFields(tokens, 2)),
                "EVT" when tokens.Length >= 5 && tokens[1] == "BTN" => ParseButton(line, tokens),
                "TEL" when tokens.Length >= 2 && tokens[1] == "SENSORS" => ParseTelemetry(line, tokens),
                "DATA" when tokens.Length >= 3 =>
                    new(ControllerMessageKind.Data, line, RequestId: tokens[1], Fields: ParseFields(tokens, 3)),
                _ => new(ControllerMessageKind.Unknown, line)
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or IndexOutOfRangeException)
        {
            return Malformed(line, "INVALID_VALUE");
        }
    }

    private static ControllerMessage ParseButton(string line, string[] tokens)
    {
        var fields = ParseFields(tokens, 4);
        var uptime = GetLong(fields, "uptime");
        return new(ControllerMessageKind.ButtonEvent, line, Fields: fields,
            Button: new ButtonEvent(tokens[2], tokens[3], uptime));
    }

    private static ControllerMessage ParseTelemetry(string line, string[] tokens)
    {
        var fields = ParseFields(tokens, 2);
        if (!fields.TryGetValue("light", out var lightText) || !fields.TryGetValue("mic", out var micText))
            return Malformed(line, "MISSING_SENSOR_VALUE");
        var light = int.Parse(lightText, CultureInfo.InvariantCulture);
        var microphone = int.Parse(micText, CultureInfo.InvariantCulture);
        if (light is < 0 or > 1023 || microphone is < 0 or > 1023)
            return Malformed(line, "SENSOR_OUT_OF_RANGE");
        return new(ControllerMessageKind.Telemetry, line, Fields: fields,
            Sensors: new SensorSnapshot(light, microphone, GetLong(fields, "uptime")));
    }

    private static IReadOnlyDictionary<string, string> ParseHello(string[] tokens)
    {
        var fields = new Dictionary<string, string>(ParseFields(tokens, 4), StringComparer.OrdinalIgnoreCase)
        {
            ["protocol"] = tokens[1],
            ["board"] = tokens[2],
            ["firmware"] = tokens[3]
        };
        return fields;
    }

    private static Dictionary<string, string> ParseFields(string[] tokens, int start)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = start; index < tokens.Length; index++)
        {
            var separator = tokens[index].IndexOf('=');
            if (separator > 0 && separator < tokens[index].Length - 1)
                fields[tokens[index][..separator]] = tokens[index][(separator + 1)..];
        }
        return fields;
    }

    private static long GetLong(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) ? long.Parse(value, CultureInfo.InvariantCulture) : 0;

    private static ControllerMessage Malformed(string raw, string reason) =>
        new(ControllerMessageKind.Malformed, raw, Error: reason);
}
