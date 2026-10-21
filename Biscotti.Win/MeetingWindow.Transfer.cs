using System.Windows;
using Biscotti.Core.Storage;
using Microsoft.Win32;

namespace Biscotti.Win;

public partial class MeetingWindow
{
    private void CopyMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_isLiveRecording || _isProcessing) return;
        if (_designPreview) { TransferStatus.Text = "Preview only. Copy would put unencrypted Markdown on the clipboard."; return; }
        if (MessageBox.Show(this,
            "Copy the current meeting text as unencrypted Markdown? Other apps and Windows clipboard history or sync, if enabled, can access it.",
            "Copy unencrypted text", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            Clipboard.SetText(MeetingStore.RenderMarkdown(CreateEditedMeeting()));
            TransferStatus.Text = "Copied current meeting text as unencrypted Markdown. Audio was not copied.";
        }
        catch (Exception ex) { TransferStatus.Text = $"Could not copy. {ex.Message}"; }
    }

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_isLiveRecording || _isProcessing) return;
        if (_designPreview) { TransferStatus.Text = "Preview only. Export would create an unencrypted Markdown file of this meeting."; return; }
        if (!TransferDisclosure.ConfirmFile(this)) return;
        var picker = new SaveFileDialog { Title = "Export meeting (unencrypted)", Filter = "Markdown (*.md)|*.md", DefaultExt = ".md", FileName = "Biscotti-meeting.md" };
        if (picker.ShowDialog(this) != true) return;
        var markdown = MeetingStore.RenderMarkdown(CreateEditedMeeting());
        ExportMarkdownButton.IsEnabled = false;
        try
        {
            await Task.Run(() => MeetingTransfer.ExportText(picker.FileName, markdown));
            TransferStatus.Text = "Exported current meeting text. The chosen Markdown file is unencrypted; audio was not included.";
        }
        catch (Exception ex) { TransferStatus.Text = $"Could not export. {ex.Message}"; }
        finally { ExportMarkdownButton.IsEnabled = true; }
    }
}
