namespace DementiaComputerButtons.Core.Abstractions;

public interface ISerialPortProvider
{
    IReadOnlyList<string> GetPortNames();
}
public interface ISerialConnectionFactory
{
    ISerialConnection Create(string portName, int baudRate);
}

public interface ISerialConnection : IDisposable
{
    string PortName { get; }
    bool IsOpen { get; }
    void Open();
    void Close();
    void WriteLine(string line);
    string? TryReadLine();
}

public interface IAsyncDelay
{
    Task Delay(TimeSpan duration, CancellationToken cancellationToken);
}
