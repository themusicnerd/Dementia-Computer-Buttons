using System.IO;
using System.Text.Json;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons.Infrastructure;

public sealed class FileLoggingService : ILoggingService
{
    private readonly object _sync = new();
    private readonly string _logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DementiaComputerButtons", "logs");

    public FileLoggingService() => Directory.CreateDirectory(_logDirectory);
    public void Information(string eventName, string message) => Write("Information", eventName, message, null);
    public void Warning(string eventName, string message) => Write("Warning", eventName, message, null);
    public void Error(string eventName, Exception exception, string message) => Write("Error", eventName, message, exception);

    private void Write(string level, string eventName, string message, Exception? exception)
    {
        var entry = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.Now, level, eventName, message, exception = exception?.ToString()
        });
        var path = Path.Combine(_logDirectory, $"dcb-{DateTime.Now:yyyyMMdd}.jsonl");
        lock (_sync)
        {
            File.AppendAllText(path, entry + Environment.NewLine);
            if (eventName.StartsWith("activity_", StringComparison.Ordinal))
            {
                var activityPath = Path.Combine(_logDirectory, $"activity-{DateTime.Now:yyyy-MM-dd}.txt");
                File.AppendAllText(activityPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {message}{Environment.NewLine}");
            }
        }
    }
}
