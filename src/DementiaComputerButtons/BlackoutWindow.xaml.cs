using System.Windows;
using System.Windows.Input;

namespace DementiaComputerButtons;

public partial class BlackoutWindow : Window
{
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
    }

    private void OnUserInput(object sender, InputEventArgs args)
    {
        args.Handled = true;
        UserWakeRequested?.Invoke(this, EventArgs.Empty);
    }
}
