using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DementiaComputerButtons;

public partial class BlackoutWindow : Window
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _use24HourClock;
    private string _dateFormat = "dddd, dd/MM/yyyy";
    public event EventHandler? UserWakeRequested;

    public BlackoutWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        PreviewKeyDown += OnUserInput;
        PreviewMouseDown += OnUserInput;
        _clockTimer.Tick += (_, _) => UpdateClock();
        BuildAnalogueFace();
        _clockTimer.Start();
        UpdateClock();
    }

    public void ConfigureClock(bool visible, string style, bool use24HourClock, string dateFormat, string colour, byte brightnessPercent, string wakePrompt)
    {
        ClockPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var analogue = style.Equals("Analogue", StringComparison.OrdinalIgnoreCase);
        DigitalClockPanel.Visibility = analogue ? Visibility.Collapsed : Visibility.Visible;
        AnalogueClockPanel.Visibility = analogue ? Visibility.Visible : Visibility.Collapsed;
        _use24HourClock = use24HourClock;
        _dateFormat = string.IsNullOrWhiteSpace(dateFormat) ? "dddd, dd/MM/yyyy" : dateFormat;
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colour));
        ClockPanel.Opacity = Math.Clamp(brightnessPercent / 100d, 0.01, 1d);
        ClockText.Foreground = brush;
        DateText.Foreground = brush;
        AnaloguePeriodText.Foreground = brush;
        WakePromptText.Foreground = brush;
        WakePromptText.Text = wakePrompt;
        foreach (var text in AnalogueClock.Children.OfType<System.Windows.Controls.TextBlock>()) text.Foreground = brush;
        foreach (var shape in AnalogueClock.Children.OfType<Shape>())
        {
            if (shape is Ellipse ellipse && ellipse.Fill is not null) ellipse.Fill = brush;
            if (shape is not Ellipse || shape.Stroke is not null) shape.Stroke = brush;
        }
        if (visible) UpdateClock();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString(_use24HourClock ? "HH:mm" : "h:mm tt");
        DateText.Text = now.ToString(_dateFormat);
        AnaloguePeriodText.Text = _use24HourClock ? now.ToString("HH:mm") : now.ToString("tt");
        SetHand(HourHand, (now.Hour % 12 + now.Minute / 60d) * 30d, 120);
        SetHand(MinuteHand, (now.Minute + now.Second / 60d) * 6d, 174);
        SetHand(SecondHand, now.Second * 6d, 188);
    }

    private void BuildAnalogueFace()
    {
        const double centre = 230;
        for (var minute = 0; minute < 60; minute++)
        {
            var angle = minute * Math.PI / 30d;
            var major = minute % 5 == 0;
            var outer = 207d;
            var inner = major ? 185d : 196d;
            var tick = new Line
            {
                X1 = centre + Math.Sin(angle) * inner,
                Y1 = centre - Math.Cos(angle) * inner,
                X2 = centre + Math.Sin(angle) * outer,
                Y2 = centre - Math.Cos(angle) * outer,
                Stroke = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(major ? "#A0A0A0" : "#505050")),
                StrokeThickness = major ? 5 : 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            System.Windows.Controls.Panel.SetZIndex(tick, 0);
            AnalogueClock.Children.Add(tick);
        }
    }

    private static void SetHand(Line hand, double degrees, double length)
    {
        const double centre = 230;
        var angle = degrees * Math.PI / 180d;
        hand.X1 = centre;
        hand.Y1 = centre;
        hand.X2 = centre + Math.Sin(angle) * length;
        hand.Y2 = centre - Math.Cos(angle) * length;
    }

    private void OnUserInput(object sender, InputEventArgs args)
    {
        args.Handled = true;
        UserWakeRequested?.Invoke(this, EventArgs.Empty);
    }
}
