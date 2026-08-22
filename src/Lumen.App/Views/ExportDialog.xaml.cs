using System.Windows;

namespace Lumen.App.Views;

/// <summary>
/// Lets the user tick the two Word export options before the Save dialog opens, instead of them
/// being silently fixed from Settings on every export.
/// </summary>
public partial class ExportDialog : Window
{
    public ExportDialog(bool pageBreaksDefault, bool pageHeadingsDefault)
    {
        InitializeComponent();

        PageBreaksCheck.IsChecked = pageBreaksDefault;
        PageHeadingsCheck.IsChecked = pageHeadingsDefault;
    }

    public bool PageBreaks { get; private set; }

    public bool PageHeadings { get; private set; }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        PageBreaks = PageBreaksCheck.IsChecked == true;
        PageHeadings = PageHeadingsCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
