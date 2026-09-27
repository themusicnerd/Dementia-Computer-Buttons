using System.Windows;

namespace DementiaComputerButtons.Infrastructure;

public interface IUserPromptService
{
    bool Confirm(string message, string title);
    void Inform(string message, string title);
}

public sealed class UserPromptService : IUserPromptService
{
    public bool Confirm(string message, string title) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public void Inform(string message, string title) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
