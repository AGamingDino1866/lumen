using System.Windows;

namespace Lumen.App.Views;

/// <summary>Prompts for the password of an encrypted PDF.</summary>
public partial class PasswordDialog : Window
{
    public PasswordDialog(string fileName)
    {
        InitializeComponent();

        FileNameText.Text = $"Enter the password for {fileName} to open it.";
        Loaded += (_, _) => PasswordBox.Focus();
    }

    /// <summary>The entered password. Held only for the lifetime of the open operation.</summary>
    public string Password { get; private set; } = string.Empty;

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox.Password;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
