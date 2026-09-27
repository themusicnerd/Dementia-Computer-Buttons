using System.Windows;

namespace DementiaComputerButtons;

public partial class BlackoutWindow : Window
{
    public BlackoutWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }
}
