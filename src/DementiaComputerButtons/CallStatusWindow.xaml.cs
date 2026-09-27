using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;

namespace DementiaComputerButtons;

public partial class CallStatusWindow : Window
{
    private readonly ICallService _calls;
    private readonly IAudioRoutingService _audio;
    private readonly System.Windows.Threading.DispatcherTimer _meterTimer;
    private CallSnapshot _current = new(CallStatus.Idle);
    private double _microphoneDisplay;
    private double _speakerDisplay;

    public CallStatusWindow(ICallService calls, IAudioRoutingService audio)
    {
        _calls = calls;
        _audio = audio;
        InitializeComponent();
        _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        _meterTimer.Tick += (_, _) => UpdateLiveDetails();
        _calls.StatusChanged += (_, snapshot) => Dispatcher.Invoke(() => Apply(snapshot));
    }

    private void Apply(CallSnapshot call)
    {
        _current = call;
        if (call.Status is CallStatus.Idle) { _meterTimer.Stop(); Hide(); return; }
        NameText.Text = string.IsNullOrWhiteSpace(call.DisplayName) ? "CALL" : call.DisplayName.ToUpperInvariant();
        InitialText.Text = NameText.Text[..1];
        StatusText.Text = call.Status switch
        {
            CallStatus.Incoming => "INCOMING CALL: PRESS ANY BUTTON TO ANSWER",
            CallStatus.Calling => "CALLING",
            CallStatus.Ringing => "RINGING",
            CallStatus.Connecting => "CONNECTING",
            CallStatus.Connected => "CONNECTED: PRESS STOP TO END",
            CallStatus.Ended => "CALL ENDED",
            CallStatus.Failed => "CALL FAILED",
            _ => "CALL"
        };
        AnswerButton.Visibility = call.Status == CallStatus.Incoming ? Visibility.Visible : Visibility.Collapsed;
        ContactPhoto.Source = LoadPhoto(call.PhotoPath);
        UpdateLiveDetails();
        if (call.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected)
            _meterTimer.Start();
        else _meterTimer.Stop();
        if (!IsVisible) Show();
        Activate();
        Topmost = true;
        if (call.Status == CallStatus.Ended)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); if (_calls.Current.Status == CallStatus.Ended) Hide(); };
            timer.Start();
        }
    }

    private void UpdateLiveDetails()
    {
        var levels = _audio.GetLevels();
        _microphoneDisplay = Math.Max(Math.Sqrt(levels.MicrophonePeak) * 100, _microphoneDisplay * 0.72);
        _speakerDisplay = Math.Max(Math.Sqrt(levels.SpeakerPeak) * 100, _speakerDisplay * 0.72);
        MicrophoneMeter.Value = Math.Clamp(_microphoneDisplay, 0, 100);
        SpeakerMeter.Value = Math.Clamp(_speakerDisplay, 0, 100);

        var method = string.IsNullOrWhiteSpace(_current.Method) ? "CALL" : _current.Method.ToUpperInvariant();
        var elapsed = _current.Status == CallStatus.Connected && _current.ConnectedAtUtc is { } connected
            ? DateTimeOffset.UtcNow - connected
            : _current.StartedAtUtc is { } started ? DateTimeOffset.UtcNow - started : TimeSpan.Zero;
        var duration = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        DetailsText.Text = _current.Status switch
        {
            CallStatus.Incoming => $"{method} • RINGING NOW",
            CallStatus.Calling => $"{method} • CALLING {duration}",
            CallStatus.Ringing => $"{method} • RINGING {duration}",
            CallStatus.Connecting => $"{method} • ANSWERED: CONNECTING",
            CallStatus.Connected => $"{method} • CONNECTED FOR {duration}",
            CallStatus.Ended when _current.ConnectedAtUtc is not null => $"{method} • CALL TIME {duration}",
            CallStatus.Ended => $"{method} • CALL ENDED",
            _ => method
        };
    }

    private static BitmapImage? LoadPhoto(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private async void Answer_Click(object sender, RoutedEventArgs e) => await _calls.AnswerIncomingAsync();
    private async void End_Click(object sender, RoutedEventArgs e)
    {
        if (_calls.Current.Status == CallStatus.Incoming) await _calls.RejectIncomingAsync();
        else await _calls.EndCallAsync();
    }
}
