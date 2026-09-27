using System.IO.Pipes;
using System.IO;
using System.Text;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Infrastructure;

public sealed class CallEventBridge(ICallService calls, ILoggingService logging) : IAsyncDisposable
{
    private const string PipeName = "DementiaComputerButtons.CallEvents.v1";
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;

    public void Start() => _listener ??= ListenAsync(_shutdown.Token);

    public static async Task<bool> TrySendAsync(string eventName, string callerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(2000, cancellationToken).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync($"{Sanitize(eventName)}\t{Sanitize(callerId)}").ConfigureAwait(false);
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return false; }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) continue;
                var parts = line.Split('\t', 2);
                if (parts.Length == 2) calls.HandleExternalEvent(parts[0], parts[1]);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception exception) { logging.Error("call_event_bridge_failed", exception, exception.Message); }
        }
    }

    private static string Sanitize(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_listener is not null)
            try { await _listener.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }
}
