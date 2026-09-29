using System.IO.Pipes;
using System.IO;
using System.Text;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Infrastructure;

public sealed class CallEventBridge(ICallService calls, ILoggingService logging) : IAsyncDisposable
{
    private const string PipeName = "DementiaComputerButtons.CallEvents.v1";
    private static readonly TimeSpan MaximumQueuedEventAge = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listener;
    private Task? _queuedEventListener;

    public void Start()
    {
        _listener ??= ListenAsync(_shutdown.Token);
        _queuedEventListener ??= ListenForQueuedEventsAsync(_shutdown.Token);
    }

    public static async Task<bool> TrySendAsync(string eventName, string callerId, CancellationToken cancellationToken = default)
    {
        var message = $"{Sanitize(eventName)}\t{Sanitize(callerId)}";
        for (var attempt = 0; attempt < 3; ++attempt)
        {
            try
            {
                await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(1000, cancellationToken).ConfigureAwait(false);
                await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                await writer.WriteLineAsync(message).ConfigureAwait(false);
                return true;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < 2)
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            }
        }

        // MicroSIP starts a short-lived process for every callback. If the pipe is
        // briefly busy or being recreated, preserve the event for the running app
        // instead of silently losing (most importantly) the remote hang-up event.
        try
        {
            var directory = QueueDirectory();
            Directory.CreateDirectory(directory);
            var name = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():D13}-{Guid.NewGuid():N}";
            var temporaryPath = Path.Combine(directory, $"{name}.tmp");
            var eventPath = Path.Combine(directory, $"{name}.event");
            await File.WriteAllTextAsync(temporaryPath, message, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, eventPath);
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

    private async Task ListenForQueuedEventsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        do
        {
            DrainQueuedEvents();
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException) { break; }
        } while (!cancellationToken.IsCancellationRequested);
    }

    private void DrainQueuedEvents()
    {
        var directory = QueueDirectory();
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*.event").OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                if (DateTime.UtcNow - File.GetCreationTimeUtc(path) > MaximumQueuedEventAge)
                {
                    File.Delete(path);
                    continue;
                }

                var parts = File.ReadAllText(path, Encoding.UTF8).Split('\t', 2);
                if (parts.Length == 2) calls.HandleExternalEvent(parts[0], parts[1]);
                File.Delete(path);
            }
            catch (Exception exception)
            {
                logging.Error("queued_call_event_failed", exception, Path.GetFileName(path));
            }
        }
    }

    private static string QueueDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DementiaComputerButtons", "CallEventQueue");

    private static string Sanitize(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_listener is not null)
            try { await _listener.ConfigureAwait(false); } catch (OperationCanceledException) { }
        if (_queuedEventListener is not null)
            try { await _queuedEventListener.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }
}
