using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons.Core.Services;

public sealed class ButtonActionService(
    IVolumeService volume,
    IConfigurationService configuration,
    ISystemStateService state,
    IAudioRoutingService audioRouting,
    IVlcService vlc,
    IBrowserService browser,
    ICallService calls,
    IOnScreenDisplayService osd,
    ILoggingService logging,
    IWindowsShellService? windowsShell = null,
    IDisplayScheduleService? displaySchedule = null) : IButtonActionService
{
    private readonly HashSet<string> _pendingShortPresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumedCallButtons = new(StringComparer.OrdinalIgnoreCase);

    public async Task HandleAsync(ButtonEvent buttonEvent, CancellationToken cancellationToken = default)
    {
        logging.Information("button_event", $"button={buttonEvent.Button}; action={buttonEvent.Action}");
        var isDown = buttonEvent.Action.Equals("DOWN", StringComparison.OrdinalIgnoreCase);
        var isUp = buttonEvent.Action.Equals("UP", StringComparison.OrdinalIgnoreCase);
        var isLong = buttonEvent.Action.Equals("LONG", StringComparison.OrdinalIgnoreCase);
        var isRepeat = buttonEvent.Action.Equals("REPEAT", StringComparison.OrdinalIgnoreCase);
        string? action = null;

        if (isDown)
            logging.Information("activity_button", $"Button pressed: {ButtonName(buttonEvent.Button)}");

        if (!isDown)
        {
            lock (_consumedCallButtons)
                if (_consumedCallButtons.Remove(buttonEvent.Button)) return;
        }
        if (isDown && calls.Current.Status == CallStatus.Incoming)
        {
            configuration.Current.ButtonMappings.TryGetValue(buttonEvent.Button, out var ringingAction);
            lock (_consumedCallButtons) _consumedCallButtons.Add(buttonEvent.Button);
            if (ringingAction is not null && (ringingAction.Equals("STOP", StringComparison.OrdinalIgnoreCase) || ringingAction.Equals("HOME", StringComparison.OrdinalIgnoreCase)))
            {
                logging.Information("activity_action", "Action: decline incoming call");
                osd.ShowMessage("CALL DECLINED");
                await calls.RejectIncomingAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                logging.Information("activity_action", $"Action: answer incoming call from {DisplayName(calls.Current.DisplayName, "unknown caller")}");
                osd.ShowMessage($"ANSWERING {DisplayName(calls.Current.DisplayName, "CALL")}");
                await calls.AnswerIncomingAsync(cancellationToken).ConfigureAwait(false);
            }
            return;
        }

        if (isLong)
        {
            lock (_pendingShortPresses) _pendingShortPresses.Remove(buttonEvent.Button);
            configuration.Current.LongPressButtonMappings.TryGetValue(buttonEvent.Button, out action);
        }
        else if (isDown && configuration.Current.LongPressButtonMappings.ContainsKey(buttonEvent.Button))
        {
            configuration.Current.ButtonMappings.TryGetValue(buttonEvent.Button, out var immediateAction);
            if (immediateAction?.Equals("STOP", StringComparison.OrdinalIgnoreCase) == true ||
                immediateAction?.Equals("HOME", StringComparison.OrdinalIgnoreCase) == true) action = immediateAction;
            else
            {
                lock (_pendingShortPresses) _pendingShortPresses.Add(buttonEvent.Button);
                return;
            }
        }
        else if (isUp)
        {
            bool pending;
            lock (_pendingShortPresses) pending = _pendingShortPresses.Remove(buttonEvent.Button);
            if (!pending) return;
            configuration.Current.ButtonMappings.TryGetValue(buttonEvent.Button, out action);
        }
        else if (isDown || isRepeat)
        {
            configuration.Current.ButtonMappings.TryGetValue(buttonEvent.Button, out action);
        }
        else return;

        if (string.IsNullOrWhiteSpace(action))
        {
            logging.Warning("button_unmapped", $"No mapping for {buttonEvent.Button}");
            return;
        }

        if (!isRepeat)
            logging.Information("activity_action", DescribeAction(action));

        var step = isRepeat ? configuration.Current.VolumeHoldStepPercent : configuration.Current.VolumeStepPercent;
        switch (action.ToUpperInvariant())
        {
            case "VOLUME_DOWN":
                volume.ChangeBy(-step);
                break;
            case "VOLUME_UP":
                volume.ChangeBy(step);
                break;
            case "MUTE":
                volume.ToggleMute();
                break;
            case "STOP":
            case "HOME":
                if (windowsShell?.CloseStartMenuIfOpen() == true)
                    logging.Information("activity_action", "Windows Start/Search panel closed");
                osd.ShowMessage(calls.IsActive ? "ENDING CALL" : "STOPPING", "RETURNING HOME");
                await StopActiveContentAsync(cancellationToken).ConfigureAwait(false);
                await audioRouting.RestoreHomeRoutingAsync(cancellationToken).ConfigureAwait(false);
                state.ReturnHome($"button:{buttonEvent.Button}");
                displaySchedule?.RearmBlackout();
                break;
            case "WATCH_TV":
                osd.ShowMessage("OPENING YOUTUBE");
                await RunModeAsync(DadConsoleState.WatchingTV, async () =>
                {
                    await vlc.StopAsync(cancellationToken).ConfigureAwait(false);
                    await calls.EndCallAsync(cancellationToken).ConfigureAwait(false);
                    await browser.OpenAsync(configuration.Current.Content.YouTubeUrl, cancellationToken).ConfigureAwait(false);
                }, buttonEvent.Button).ConfigureAwait(false);
                break;
            case "OPEN_SPOTIFY":
                osd.ShowMessage("OPENING SPOTIFY");
                await RunModeAsync(DadConsoleState.ListeningSpotify, async () =>
                {
                    await vlc.StopAsync(cancellationToken).ConfigureAwait(false);
                    await calls.EndCallAsync(cancellationToken).ConfigureAwait(false);
                    await browser.OpenAsync(configuration.Current.Content.SpotifyUrl, cancellationToken).ConfigureAwait(false);
                }, buttonEvent.Button).ConfigureAwait(false);
                break;
            case "PLAY_VIDEO":
                osd.ShowMessage($"OPENING {DisplayName(configuration.Current.Content.VideoDisplayName, "VIDEO")}");
                await RunModeAsync(DadConsoleState.PlayingVideo, async () =>
                {
                    await browser.CloseManagedContentAsync(cancellationToken).ConfigureAwait(false);
                    await calls.EndCallAsync(cancellationToken).ConfigureAwait(false);
                    await vlc.PlayAsync(configuration.Current.Content.VideoPath, cancellationToken).ConfigureAwait(false);
                }, buttonEvent.Button).ConfigureAwait(false);
                break;
            case "CALL_ADRIAN":
                await StartCallAsync("ADRIAN", DadConsoleState.CallingAdrian, buttonEvent.Button, cancellationToken).ConfigureAwait(false);
                break;
            case "CALL_YVONNE":
                await StartCallAsync("YVONNE", DadConsoleState.CallingMum, buttonEvent.Button, cancellationToken).ConfigureAwait(false);
                break;
            case "SPEAKERS_TOGGLE":
                if (!audioRouting.IsAvailable)
                {
                    osd.ShowMessage("AUDIO NOT SET UP", "SELECT OUTPUTS AND MICROPHONES");
                    logging.Warning("speakers_toggle_unavailable", "Speaker/headphone outputs and microphones have not all been selected.");
                }
                else
                {
                    osd.ShowMessage(audioRouting.SpeakersMuted ? "TURNING ON SPEAKERS" : "MUTING SPEAKERS",
                        audioRouting.SpeakersMuted ? null : "PUT ON HEADPHONES");
                    if (!await audioRouting.ToggleSpeakersAsync(cancellationToken).ConfigureAwait(false))
                    logging.Warning("speakers_toggle_unavailable", "Windows could not switch the selected audio output device.");
                }
                break;
            case "BLACKOUT":
                displaySchedule?.BlackoutNow();
                break;
            default:
                logging.Warning("button_action_unknown", $"Unknown mapped action {action}");
                break;
        }
    }

    private async Task StartCallAsync(string contact, DadConsoleState target, string button, CancellationToken cancellationToken)
    {
        var settings = contact == "ADRIAN" ? configuration.Current.Calls.Adrian : configuration.Current.Calls.Yvonne;
        if (calls.IsCallingBlocked)
        {
            var quiet = configuration.Current.Calls.QuietHours;
            osd.ShowMessage("CALLING UNAVAILABLE", $"QUIET HOURS {quiet.From}–{quiet.Until}");
            logging.Information("activity_call", $"Call to {DisplayName(settings.DisplayName, contact)} blocked during quiet hours");
            return;
        }
        osd.ShowMessage($"CALLING {DisplayName(settings.DisplayName, contact)}", $"ON {DisplayName(settings.Method, "UNCONFIGURED")}");
        await RunModeAsync(target, async () =>
        {
            await browser.CloseManagedContentAsync(cancellationToken).ConfigureAwait(false);
            await vlc.StopAsync(cancellationToken).ConfigureAwait(false);
            await calls.StartCallAsync(contact, cancellationToken).ConfigureAwait(false);
        }, button).ConfigureAwait(false);
    }

    private async Task StopActiveContentAsync(CancellationToken cancellationToken)
    {
        await browser.CloseManagedContentAsync(cancellationToken).ConfigureAwait(false);
        if (configuration.Current.Content.StopVideoOnHome)
            await vlc.StopAsync(cancellationToken).ConfigureAwait(false);
        await calls.EndCallAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunModeAsync(DadConsoleState target, Func<Task> start, string button)
    {
        try
        {
            await start().ConfigureAwait(false);
            state.TransitionTo(target, $"button:{button}");
        }
        catch (Exception exception)
        {
            logging.Error("mode_start_failed", exception, $"state={target}; button={button}");
            if (target is DadConsoleState.CallingAdrian or DadConsoleState.CallingMum)
            {
                var contact = target == DadConsoleState.CallingAdrian
                    ? configuration.Current.Calls.Adrian.DisplayName
                    : configuration.Current.Calls.Yvonne.DisplayName;
                osd.ShowMessage($"COULD NOT CALL {DisplayName(contact, "CONTACT")}", "CHECK CALL SETTINGS");
            }
            else osd.ShowMessage("COULD NOT START", FriendlyState(target));
            state.TransitionTo(DadConsoleState.Error, $"failed:{button}");
        }
    }

    private static string DisplayName(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FriendlyState(DadConsoleState state) => state switch
    {
        DadConsoleState.WatchingTV => "YOUTUBE",
        DadConsoleState.ListeningSpotify => "SPOTIFY",
        DadConsoleState.PlayingVideo => "VIDEO",
        _ => "ACTION"
    };

    private string DescribeAction(string action) => action.ToUpperInvariant() switch
    {
        "VOLUME_DOWN" => "Action: volume down",
        "VOLUME_UP" => "Action: volume up",
        "MUTE" => "Action: toggle mute",
        "STOP" or "HOME" => calls.IsActive ? "Action: end call and return Home" : "Action: stop and return Home",
        "WATCH_TV" => $"Action: open YouTube ({configuration.Current.Content.YouTubeUrl})",
        "OPEN_SPOTIFY" => $"Action: open Spotify ({configuration.Current.Content.SpotifyUrl})",
        "PLAY_VIDEO" => $"Action: play {DisplayName(configuration.Current.Content.VideoDisplayName, "video")} ({configuration.Current.Content.VideoPath})",
        "CALL_ADRIAN" => $"Action: call {DisplayName(configuration.Current.Calls.Adrian.DisplayName, "contact 1")} on {DisplayName(configuration.Current.Calls.Adrian.Method, "unconfigured")}",
        "CALL_YVONNE" => $"Action: call {DisplayName(configuration.Current.Calls.Yvonne.DisplayName, "contact 2")} on {DisplayName(configuration.Current.Calls.Yvonne.Method, "unconfigured")}",
        "SPEAKERS_TOGGLE" => audioRouting.SpeakersMuted ? "Action: switch to speakers" : "Action: switch to headphones",
        "BLACKOUT" => "Action: black out display",
        _ => $"Action: {action}"
    };

    private static string ButtonName(string button) => button.ToUpperInvariant() switch
    {
        "PANEL_1" or "LEFT" => "Volume down",
        "PANEL_2" or "RIGHT" => "Volume up",
        "PANEL_3" => "Contact 1",
        "PANEL_4" => "Contact 2",
        "PANEL_5" => "TV / Spotify",
        "PANEL_6" => "Video",
        "PANEL_7" => "Stop / Home",
        "PANEL_8" => "Speakers / headphones",
        _ => button
    };
}
