using System.Windows;
using DementiaComputerButtons.ViewModels;

namespace DementiaComputerButtons;

public partial class MainWindow : Window
{
    public MainWindow(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
