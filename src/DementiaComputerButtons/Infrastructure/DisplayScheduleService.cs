using System.Windows.Threading;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Infrastructure;

public sealed class DisplayScheduleService(
    IConfigurationService configuration,
    ICallService calls,
    BlackoutWindow blackout,
    ILoggingService logging) : IDisplayScheduleService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private bool _manuallyWoken;
    private bool _manualBlackout;
    private bool _isBlackoutActive;

    public bool IsBlackoutActive => _isBlackoutActive;
    public event EventHandler<bool>? BlackoutStateChanged;

    public void Start()
    {
        _timer.Tick += (_, _) => Evaluate();
        blackout.UserWakeRequested += (_, _) => blackout.Dispatcher.Invoke(() =>
        {
            _manualBlackout = false;
            _manuallyWoken = true;
            SetBlackoutActive(false);
            logging.Information("activity_display", "Scheduled screen blackout temporarily disabled by keyboard or mouse");
        });
        calls.StatusChanged += (_, snapshot) => blackout.Dispatcher.Invoke(() =>
        {
            if (snapshot.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected) SetBlackoutActive(false);
            else Evaluate();
        });
        _timer.Start();
        Evaluate();
    }

    public void Evaluate()
    {
        blackout.Dispatcher.Invoke(EvaluateCore);
    }

    private void EvaluateCore()
    {
        var schedule = configuration.Current.DisplaySchedule;
        if (calls.Current.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected)
        {
            SetBlackoutActive(false);
            return;
        }
        if (_manualBlackout)
        {
            SetBlackoutActive(true);
            return;
        }
        if (!schedule.Enabled)
        {
            _manuallyWoken = false;
            SetBlackoutActive(false);
            return;
        }
        if (!TimeOnly.TryParseExact(schedule.BlackoutFrom, "HH:mm", out _) ||
            !TimeOnly.TryParseExact(schedule.ResumeAt, "HH:mm", out _))
        {
            SetBlackoutActive(false);
            logging.Warning("display_schedule_invalid", $"from={schedule.BlackoutFrom}; until={schedule.ResumeAt}");
            return;
        }
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var shouldBlackout = DailyTimeRange.Contains(true, schedule.BlackoutFrom, schedule.ResumeAt, now);
        if (!shouldBlackout) _manuallyWoken = false;
        if (shouldBlackout)
        {
            if (_manuallyWoken) { SetBlackoutActive(false); return; }
            SetBlackoutActive(true);
        }
        else SetBlackoutActive(false);
    }

    private void SetBlackoutActive(bool active)
    {
        if (active)
        {
            if (!blackout.IsVisible) blackout.Show();
            blackout.Topmost = true;
            blackout.Activate();
            blackout.Focus();
        }
        else blackout.Hide();

        if (_isBlackoutActive == active) return;
        _isBlackoutActive = active;
        BlackoutStateChanged?.Invoke(this, active);
    }

    public void RearmBlackout()
    {
        blackout.Dispatcher.Invoke(() =>
        {
            _manuallyWoken = false;
            logging.Information("activity_display", "Scheduled screen blackout re-armed by Stop/Home");
            EvaluateCore();
        });
    }

    public void BlackoutNow()
    {
        blackout.Dispatcher.Invoke(() =>
        {
            _manualBlackout = true;
            _manuallyWoken = false;
            logging.Information("activity_display", "Display blacked out by Stop/Home long press");
            EvaluateCore();
        });
    }
}
