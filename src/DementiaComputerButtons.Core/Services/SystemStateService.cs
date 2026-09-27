using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Core.Services;

public sealed class SystemStateService(ILoggingService logging) : ISystemStateService
{
    private readonly object _sync = new();
    private DadConsoleState _current = DadConsoleState.Home;

    public DadConsoleState Current { get { lock (_sync) return _current; } }
    public event EventHandler<DadConsoleState>? StateChanged;

    public void TransitionTo(DadConsoleState target, string reason)
    {
        DadConsoleState previous;
        lock (_sync)
        {
            previous = _current;
            if (previous == target) return;
            _current = target;
        }
        logging.Information("state_transition", $"{previous} -> {target}; reason={reason}");
        logging.Information("activity_state", Describe(target));
        StateChanged?.Invoke(this, target);
    }

    public void ReturnHome(string reason) => TransitionTo(DadConsoleState.Home, reason);

    private static string Describe(DadConsoleState state) => state switch
    {
        DadConsoleState.Home => "Home / no media active",
        DadConsoleState.WatchingTV => "YouTube is playing",
        DadConsoleState.ListeningSpotify => "Spotify is playing",
        DadConsoleState.PlayingVideo => "Orientation video is playing",
        DadConsoleState.CallingAdrian => "Calling contact 1",
        DadConsoleState.CallingMum => "Calling contact 2",
        DadConsoleState.IncomingCall => "Incoming call",
        DadConsoleState.InCall => "Call connected",
        DadConsoleState.Error => "Action failed",
        _ => state.ToString()
    };
}
