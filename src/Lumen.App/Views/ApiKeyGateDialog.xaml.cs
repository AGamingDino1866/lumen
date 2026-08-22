using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lumen.App.Services;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

/// <summary>
/// First-run key entry. Validates against the API before persisting, so an invalid key is
/// reported here rather than as a failure on every page of the user's first run.
/// </summary>
public partial class ApiKeyGateDialog : Window
{
    private readonly MainViewModel _viewModel;
    private bool _revealed;
    private bool _syncing;

    public ApiKeyGateDialog(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) => KeyBox.Focus();
    }

    private string CurrentKey => _revealed ? KeyPlainBox.Text : KeyBox.Password;

    private void OnKeyChanged(object sender, RoutedEventArgs e) => UpdateSaveState();

    private void OnPlainKeyChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing)
        {
            UpdateSaveState();
        }
    }

    private void UpdateSaveState()
    {
        SaveButton.IsEnabled = CurrentKey.Trim().Length > 0;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnToggleReveal(object sender, RoutedEventArgs e)
    {
        _syncing = true;

        _revealed = !_revealed;

        if (_revealed)
        {
            KeyPlainBox.Text = KeyBox.Password;
            KeyPlainBox.Visibility = Visibility.Visible;
            KeyBox.Visibility = Visibility.Collapsed;
            KeyPlainBox.Focus();
            KeyPlainBox.CaretIndex = KeyPlainBox.Text.Length;
        }
        else
        {
            KeyBox.Password = KeyPlainBox.Text;
            KeyBox.Visibility = Visibility.Visible;
            KeyPlainBox.Visibility = Visibility.Collapsed;
            KeyBox.Focus();
        }

        if (TryFindResource(_revealed ? "Lumen.Icon.EyeSlash" : "Lumen.Icon.Eye") is Geometry geometry)
        {
            RevealIcon.Data = geometry;
        }

        _syncing = false;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var key = CurrentKey.Trim();

        if (key.Length == 0)
        {
            return;
        }

        SaveButton.IsEnabled = false;
        SaveButton.Content = "Checking…";
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Validate before persisting. A key that does not work should never reach disk.
            var valid = await _viewModel.TrySetApiKeyAsync(key, cts.Token);

            if (valid)
            {
                DialogResult = true;
                return;
            }

            ShowError("That key was not accepted. Check that you copied all of it and that the Generative Language API is enabled.");
        }
        catch (OperationCanceledException)
        {
            ShowError("Checking the key timed out. Check your internet connection and try again.");
        }
        catch (Exception)
        {
            ShowError("Lumen could not reach Gemini to check that key. Check your connection and try again.");
        }
        finally
        {
            SaveButton.Content = "Save and continue";
            SaveButton.IsEnabled = CurrentKey.Trim().Length > 0;
        }
    }

    private void ShowError(string message)
    {
        // The message never echoes the key back, not even partially.
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnOpenKeyPage(object sender, RoutedEventArgs e)
    {
        try
        {
            new DialogService().OpenUrl("https://aistudio.google.com/apikey");
        }
        catch (Exception)
        {
            // Opening a browser is a convenience; a failure must not break key entry.
        }
    }
}
