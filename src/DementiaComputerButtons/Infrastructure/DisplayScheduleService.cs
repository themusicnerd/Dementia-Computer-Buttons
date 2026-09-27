using System.Windows.Threading;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Infrastructure;

public sealed class DisplayScheduleService(
    IConfigurationService configuration,
    ICallService calls,
    BlackoutWindow blackout,
    ILoggingService logging)
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };

    public void Start()
    {
        _timer.Tick += (_, _) => Evaluate();
        calls.StatusChanged += (_, snapshot) => blackout.Dispatcher.Invoke(() =>
        {
            if (snapshot.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected) blackout.Hide();
            else Evaluate();
        });
        _timer.Start();
        Evaluate();
    }

    public void Evaluate()
    {
        blackout.Dispatcher.Invoke(() =>
        {
            var schedule = configuration.Current.DisplaySchedule;
            if (!schedule.Enabled || calls.Current.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected)
            {
                blackout.Hide();
                return;
            }
            if (!TimeOnly.TryParseExact(schedule.BlackoutFrom, "HH:mm", out var from) ||
                !TimeOnly.TryParseExact(schedule.ResumeAt, "HH:mm", out var until))
            {
                blackout.Hide();
                logging.Warning("display_schedule_invalid", $"from={schedule.BlackoutFrom}; until={schedule.ResumeAt}");
                return;
            }
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var shouldBlackout = from == until || (from < until ? now >= from && now < until : now >= from || now < until);
            if (shouldBlackout)
            {
                if (!blackout.IsVisible) blackout.Show();
                blackout.Topmost = true;
            }
            else blackout.Hide();
        });
    }
}
