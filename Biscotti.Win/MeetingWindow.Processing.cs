using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using Biscotti.Core.Models;

namespace Biscotti.Win;

public partial class MeetingWindow
{
    private bool _processingActionRunning;

    private void RefreshProcessingPanel()
    {
        if (ProcessingPanel is null) return;
        var status = _meeting.ProcessingStatus;
        var hasTranscript = _meeting.Transcript.Count > 0;
        var show = status != MeetingProcessingStatus.Complete || hasTranscript;
        ProcessingPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ProcessingStatusText.Text = _processingActionRunning ? "Working locally… Previous saved results remain available until the new result is ready."
            : status switch
            {
                MeetingProcessingStatus.Recording => "Recording stopped unexpectedly. Recover saved audio to review it, then retry transcription when ready.",
                MeetingProcessingStatus.Transcribing => "Transcription stopped unexpectedly. Retry from the saved audio.",
                MeetingProcessingStatus.TranscriptionFailed => "Transcription could not finish. Your previous transcript is intact. Check the model or protected audio, then retry.",
                MeetingProcessingStatus.TranscriptionPending => "Saved audio recovered. You can play it now or retry transcription.",
                MeetingProcessingStatus.SummaryPending => "Transcript saved. Any previous summary is preserved; generate a summary to match the current transcript.",
                MeetingProcessingStatus.Summarizing => "Summary generation stopped unexpectedly. Retry using the transcript already saved.",
                MeetingProcessingStatus.SummaryFailed => "The local summary could not be generated. Any previous summary and action items are intact.",
                _ when !hasTranscript => "No transcript is available yet.",
                _ when string.IsNullOrWhiteSpace(_meeting.Summary) => "Transcript is ready. Generate a summary with your local Ollama model.",
                _ => "Regenerate the summary from this transcript using your local Ollama model.",
            };

        RecoverAudioButton.Visibility = status == MeetingProcessingStatus.Recording ? Visibility.Visible : Visibility.Collapsed;
        RetryTranscriptionButton.Visibility = _meeting.EndedUtc is not null &&
            status is not (MeetingProcessingStatus.Recording or MeetingProcessingStatus.Summarizing)
            ? Visibility.Visible : Visibility.Collapsed;
        RegenerateSummaryButton.Visibility = hasTranscript ? Visibility.Visible : Visibility.Collapsed;
        RegenerateSummaryButton.Content = string.IsNullOrWhiteSpace(_meeting.Summary) ? "Generate summary" : "Regenerate summary";
        RecoverAudioButton.IsEnabled = !_processingActionRunning && !_isLiveRecording && _recoverInterruptedMeeting is not null;
        RetryTranscriptionButton.IsEnabled = !_processingActionRunning && !_isLiveRecording && _retryTranscription is not null;
        RegenerateSummaryButton.IsEnabled = !_processingActionRunning && !_isLiveRecording && _regenerateSummary is not null;
    }

    private async void RecoverAudio_Click(object sender, RoutedEventArgs e) =>
        await RunProcessingActionAsync(RecoverAudioButton, _recoverInterruptedMeeting, "Saved audio is ready to review. Retry transcription when you are ready.");

    private async void RetryTranscription_Click(object sender, RoutedEventArgs e) =>
        await RunProcessingActionAsync(RetryTranscriptionButton, _retryTranscription, "Transcript updated. Your existing summary was preserved; regenerate it to match.");

    private async void RegenerateSummary_Click(object sender, RoutedEventArgs e) =>
        await RunProcessingActionAsync(RegenerateSummaryButton, _regenerateSummary, "Summary and action items saved locally.");

    private async Task RunProcessingActionAsync(System.Windows.Controls.Button button,
        Func<Meeting, CancellationToken, Task>? action, string success)
    {
        if (action is null || _processingActionRunning || _isLiveRecording || _isProcessing) return;
        _processingActionRunning = true;
        RefreshProcessingPanel();
        string? finalMessage = null;
        try
        {
            await action(_meeting, CancellationToken.None);
            RefreshTranscript();
            finalMessage = success;
        }
        catch (Exception ex)
        {
            RefreshTranscript();
            finalMessage = DescribeProcessingFailure(ex);
        }
        finally
        {
            _processingActionRunning = false;
            RefreshProcessingPanel();
            if (finalMessage is not null) ProcessingStatusText.Text = finalMessage;
        }
    }

    private static string DescribeProcessingFailure(Exception ex) => ex switch
    {
        HttpRequestException => "Local Ollama is unavailable. Start Ollama, check Settings for the selected model, then retry the summary.",
        CryptographicException => "Windows could not authenticate or unlock this meeting's protected audio. Check the Windows profile that recorded it, then retry; your previous transcript remains available.",
        DirectoryNotFoundException => "A local model or saved meeting folder is missing. Open Settings to check the selected Whisper model, then retry.",
        FileNotFoundException => "A local model or saved audio file is missing. Open Settings to download the selected Whisper model, then retry.",
        UnauthorizedAccessException or IOException => "Saved meeting data could not be read or protected on disk. Check storage access, then retry.",
        InvalidDataException => ex.Message,
        _ => ex.Message,
    };

}
