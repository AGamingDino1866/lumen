using System.IO;
using System.Windows;
using Lumen.App.Services;

namespace Lumen.App.Views;

/// <summary>
/// Asks once, per launch, whether to install a release found on GitHub. Declining just closes
/// the dialog -- Lumen checks again next time it starts, never again this session.
/// </summary>
public partial class UpdateDialog : Window
{
    private readonly IUpdateCheckService _updateCheck;
    private readonly string _installerUrl;

    public UpdateDialog(IUpdateCheckService updateCheck, string version, string installerUrl)
    {
        _updateCheck = updateCheck;
        _installerUrl = installerUrl;

        InitializeComponent();

        BodyText.Text = $"Version {version} is available. Update now?";
    }

    private void OnDecline(object sender, RoutedEventArgs e) => Close();

    private async void OnAccept(object sender, RoutedEventArgs e)
    {
        QuestionPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        TitleText.Text = "Updating Lumen";
        BodyText.Text = string.Empty;

        var progress = new Progress<double>(percent =>
        {
            DownloadProgress.Value = percent;
            ProgressText.Text = $"Downloading update… {percent:0}%";
        });

        var installerPath = Path.Combine(Path.GetTempPath(), $"Lumen-Update-{Guid.NewGuid():N}.exe");

        try
        {
            await _updateCheck.DownloadInstallerAsync(_installerUrl, installerPath, progress, CancellationToken.None);

            // Never returns: the installer takes over and this process exits.
            _updateCheck.LaunchInstallerAndExit(installerPath);
        }
        catch (Exception)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            TitleText.Text = "Update failed";
            ErrorText.Text = "The update could not be downloaded. Check your connection and try again later.";
        }
    }
}
