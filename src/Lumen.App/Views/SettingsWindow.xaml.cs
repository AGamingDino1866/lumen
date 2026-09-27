using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lumen.App.Services;
using Lumen.App.ViewModels;
using Lumen.Core.Settings;

namespace Lumen.App.Views;

/// <summary>Settings: key management, model, theme, and export defaults.</summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IThemeService _theme;
    private bool _loading = true;

    public SettingsWindow(MainViewModel viewModel, IThemeService theme)
    {
        _viewModel = viewModel;
        _theme = theme;

        InitializeComponent();

        MaskedKeyText.Text = viewModel.MaskedApiKey;

        foreach (var model in LumenSettings.SupportedModels)
        {
            ModelCombo.Items.Add(new ComboBoxItem { Content = model, Tag = model });
        }

        SelectByTag(ModelCombo, viewModel.SelectedModel);
        SelectByTag(ThemeCombo, viewModel.Settings.ThemeMode);
        PageBreaksCheck.IsChecked = viewModel.Settings.ExportPageBreaks;
        PageHeadingsCheck.IsChecked = viewModel.Settings.ExportPageHeadings;

        _loading = false;
    }

    private static void SelectByTag(Selector combo, string tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (Equals(item.Tag as string, tag))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModelCombo.SelectedItem is not ComboBoxItem { Tag: string model })
        {
            return;
        }

        _viewModel.SelectedModel = model;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedItem is not ComboBoxItem { Tag: string mode })
        {
            return;
        }

        _theme.Apply(ThemeService.Parse(mode));
        _viewModel.Settings.ThemeMode = mode;
        _viewModel.PersistSettings();
    }

    private void OnExportOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _viewModel.Settings.ExportPageBreaks = PageBreaksCheck.IsChecked == true;
        _viewModel.Settings.ExportPageHeadings = PageHeadingsCheck.IsChecked == true;
        _viewModel.PersistSettings();
    }

    private void OnReplaceKey(object sender, RoutedEventArgs e)
    {
        var gate = new ApiKeyGateDialog(_viewModel) { Owner = this };

        if (gate.ShowDialog() == true)
        {
            MaskedKeyText.Text = _viewModel.MaskedApiKey;
        }
    }

    private void OnDeleteKey(object sender, RoutedEventArgs e)
    {
        // Deleting a key is destructive and not obviously reversible, so it is confirmed.
        var confirm = MessageBox.Show(
            "Remove the stored API key? Lumen will ask for a key again the next time you extract.",
            "Lumen",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        _viewModel.ClearApiKey();
        MaskedKeyText.Text = _viewModel.MaskedApiKey;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
