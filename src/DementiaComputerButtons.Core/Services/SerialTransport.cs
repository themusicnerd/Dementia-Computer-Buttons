using System.IO.Ports;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Core.Services;

public sealed class SerialPortProvider : ISerialPortProvider
{
    public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames()
        .OrderBy(PortSortKey, StringComparer.OrdinalIgnoreCase).ToArray();

    private static string PortSortKey(string port) => port.PadLeft(12, '0');
}
public sealed class SerialConnectionFactory : ISerialConnectionFactory
{
    public ISerialConnection Create(string portName, int baudRate) => new SerialConnection(portName, baudRate);
}

public sealed class SerialConnection : ISerialConnection
{
    private readonly SerialPort _port;

    public SerialConnection(string portName, int baudRate)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\n",
            ReadTimeout = 150,
            WriteTimeout = 1000,
            DtrEnable = true,
            RtsEnable = true
        };
    }

    public string PortName => _port.PortName;
    public bool IsOpen => _port.IsOpen;
    public void Open() => _port.Open();
    public void Close() { if (_port.IsOpen) _port.Close(); }
    public void WriteLine(string line) => _port.Write(line + "\n");

    public string? TryReadLine()
    {
        try { return _port.ReadLine().TrimEnd('\r', '\n'); }
        catch (TimeoutException) { return null; }
    }

    public void Dispose() => _port.Dispose();
}

public sealed class SystemAsyncDelay : IAsyncDelay
{
    public Task Delay(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
}
