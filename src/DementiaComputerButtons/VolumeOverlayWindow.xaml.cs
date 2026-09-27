using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DementiaComputerButtons.Core.Abstractions;

namespace DementiaComputerButtons;

public partial class VolumeOverlayWindow : Window, IOnScreenDisplayService
{
    private const int ExtendedStyleIndex = -20;
    private const int NoActivate = 0x08000000;
    private const int ToolWindow = 0x00000080;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };

    public VolumeOverlayWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        Loaded += (_, _) => PositionAtBottom();
    }

    public void ShowVolume(float percent, bool muted)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowVolume(percent, muted));
            return;
        }

        var value = Math.Clamp((int)Math.Round(percent), 0, 100);
        PercentText.Text = $"{value}%";
        MuteText.Text = muted ? "SPEAKERS MUTED" : "VOLUME";
        MuteText.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        MuteText.TextAlignment = TextAlignment.Left;
        PercentText.Visibility = Visibility.Visible;
        VolumeBar.Visibility = Visibility.Visible;
        StatusDetailText.Visibility = Visibility.Collapsed;
        FilledColumn.Width = new GridLength(muted ? 0 : Math.Max(0.01, value), GridUnitType.Star);
        EmptyColumn.Width = new GridLength(muted ? 100 : Math.Max(0.01, 100 - value), GridUnitType.Star);
        PositionAtBottom();
        if (!IsVisible) Show();
        Topmost = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void ShowMessage(string headline, string? detail = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowMessage(headline, detail));
            return;
        }
        MuteText.Text = headline.ToUpperInvariant();
        MuteText.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        MuteText.TextAlignment = TextAlignment.Center;
        PercentText.Visibility = Visibility.Collapsed;
        VolumeBar.Visibility = Visibility.Collapsed;
        StatusDetailText.Text = detail?.ToUpperInvariant() ?? string.Empty;
        StatusDetailText.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        PositionAtBottom();
        if (!IsVisible) Show();
        Topmost = true;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void PositionAtBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - Height - 34;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(styles | NoActivate | ToolWindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
