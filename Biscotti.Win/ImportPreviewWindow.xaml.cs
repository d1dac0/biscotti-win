using System.Windows;
using Biscotti.Core.Storage;

namespace Biscotti.Win;

public partial class ImportPreviewWindow : Window
{
    public ImportPreviewWindow(ImportPreview preview)
    {
        InitializeComponent();
        CountsText.Text = preview.Counts;
        ErrorsList.ItemsSource = preview.Errors;
        ImportButton.IsEnabled = preview.Meetings.Count > 0;
    }

    private void Import_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
