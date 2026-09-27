using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Protocol;

namespace DementiaComputerButtons.Tests;

public sealed class ProtocolParserTests
{
    [Fact]
    public void ParsesHandshakeIdentity()
    {
        var message = ProtocolParser.Parse("HELLO DCB/1 KS0501 0.1.0 caps=LED,RGB matrix=1");
        Assert.Equal(ControllerMessageKind.Hello, message.Kind);
        Assert.Equal("KS0501", message.Fields!["board"]);
        Assert.Equal("0.1.0", message.Fields["firmware"]);
    }

    [Fact]
    public void ParsesButtonAndUptime()
    {
        var message = ProtocolParser.Parse("EVT BTN LEFT DOWN uptime=1234");
        Assert.Equal(new ButtonEvent("LEFT", "DOWN", 1234), message.Button);
    }

    [Fact]
    public void ParsesSensorTelemetry()
    {
        var message = ProtocolParser.Parse("TEL SENSORS light=321 mic=44 uptime=9000");
        Assert.Equal(new SensorSnapshot(321, 44, 9000), message.Sensors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TEL SENSORS light=nope mic=4")]
    [InlineData("TEL SENSORS light=2048 mic=4")]
    public void RejectsMalformedMessages(string line) =>
        Assert.Equal(ControllerMessageKind.Malformed, ProtocolParser.Parse(line).Kind);

    [Fact]
    public void RejectsOversizedMessage() =>
        Assert.Equal("LINE_TOO_LONG", ProtocolParser.Parse(new string('X', 96)).Error);
}
