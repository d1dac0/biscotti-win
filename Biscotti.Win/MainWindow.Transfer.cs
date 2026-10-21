using System.Windows;
using Biscotti.Core.Models;
using Biscotti.Core.Storage;
using Microsoft.Win32;

namespace Biscotti.Win;

public partial class MainWindow
{
    private CancellationTokenSource? _transferCancellation;

    private void UpdateTransferControls(RecordingState? state = null)
    {
        if (ImportCsvButton is null) return;
        var enabled = _historyReady && _transferCancellation is null && (state ?? _meetings.State) == RecordingState.Idle;
        ImportCsvButton.IsEnabled = enabled;
        ExportCsvButton.IsEnabled = enabled && _historyDocuments.Length > 0;
        CancelTransferButton.Visibility = _transferCancellation is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e) => _transferCancellation?.Cancel();

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_designPreview) { TransferStatus.Text = "Preview only. CSV import does not open files or change saved meetings."; return; }
        if (!_historyReady || _transferCancellation is not null || _meetings.State != RecordingState.Idle) return;
        var picker = new OpenFileDialog { Title = "Import Biscotti Windows CSV v1", Filter = "Biscotti CSV (*.csv)|*.csv" };
        if (picker.ShowDialog(this) != true) return;
        using var cancellation = new CancellationTokenSource();
        _transferCancellation = cancellation;
        UpdateTransferControls();
        try
        {
            TransferStatus.Text = "Checking CSV rows… Cancel leaves history unchanged.";
            var preview = await Task.Run(() => MeetingTransfer.PreviewFile(picker.FileName,
                MeetingStore.LoadMetadataOnly().Select(m => m.Id), cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            var dialog = new ImportPreviewWindow(preview) { Owner = this };
            if (dialog.ShowDialog() != true) { TransferStatus.Text = "Import cancelled. No meetings were added."; return; }
            TransferStatus.Text = "Importing valid rows into protected storage…";
            var progress = new Progress<int>(count => TransferStatus.Text = $"Imported {count} of {preview.Meetings.Count} valid rows…");
            var result = await Task.Run(() => MeetingTransfer.Import(preview, cancellation.Token, progress));
            await LoadInitialMeetingsAsync();
            TransferStatus.Text = $"{result.Imported} imported · {result.Duplicates} duplicates skipped · {preview.InvalidRows} invalid rows. "
                + (result.Cancelled ? "Cancelled; completed imports were kept. " : "") + result.Error;
        }
        catch (OperationCanceledException) { TransferStatus.Text = "Import cancelled. No meetings were added."; }
        catch (Exception ex) { TransferStatus.Text = $"Import could not finish. {ex.Message}"; }
        finally { _transferCancellation = null; UpdateTransferControls(); }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_designPreview) { TransferStatus.Text = "Preview only. Export would create an unencrypted CSV of all saved meetings, without audio."; return; }
        if (!_historyReady || _transferCancellation is not null || _meetings.State != RecordingState.Idle) return;
        if (!TransferDisclosure.ConfirmFile(this)) return;
        var picker = new SaveFileDialog { Title = "Export all saved meetings (unencrypted)", Filter = "CSV (*.csv)|*.csv", FileName = "Biscotti-meetings.csv", DefaultExt = ".csv" };
        if (picker.ShowDialog(this) != true) return;
        using var cancellation = new CancellationTokenSource();
        _transferCancellation = cancellation;
        UpdateTransferControls();
        try
        {
            TransferStatus.Text = "Exporting all saved meeting text…";
            var count = await Task.Run(() =>
            {
                var meetings = MeetingStore.LoadMetadataOnly();
                var csv = MeetingTransfer.RenderCsv(meetings, cancellation.Token);
                MeetingTransfer.ExportText(picker.FileName, csv, cancellation.Token);
                return meetings.Count;
            });
            TransferStatus.Text = $"Exported {count} meetings without audio. The chosen CSV file is unencrypted.";
        }
        catch (OperationCanceledException) { TransferStatus.Text = "Export cancelled. The destination was not changed."; }
        catch (Exception ex) { TransferStatus.Text = $"Export could not finish. {ex.Message}"; }
        finally { _transferCancellation = null; UpdateTransferControls(); }
    }
}

internal static class TransferDisclosure
{
    internal static bool ConfirmFile(Window owner) => MessageBox.Show(owner,
        "The exported file contains unencrypted meeting text, including transcripts, summaries and notes. Anyone with access to the chosen folder can read it. Audio is not included.\n\nContinue to choose a file?",
        "Unencrypted local export", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
