using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Biscotti.Core;
using Biscotti.Core.Models;

namespace Biscotti.Win;

public partial class MainWindow
{
    private MeetingSearchDocument[] _historyDocuments = [];
    private CancellationTokenSource? _historySearchCancellation;
    private bool _historyReady;
    private int _historyLoadVersion;
    private string? _historyFailure;

    private Meeting? SelectedHistoryMeeting => (MeetingList.SelectedItem as MeetingHistoryResult)?.Meeting;

    private void SetHistory(IReadOnlyList<Meeting> meetings, Guid? selectedId = null)
    {
        _historyDocuments = meetings.Select(MeetingSearchDocument.FromMeeting).ToArray();
        _historyReady = true;
        _historyFailure = null;
        UpdateTransferControls();
        _ = ApplyHistorySearchAsync(selectedId, debounce: false);
    }

    private void ShowHistoryFailure()
    {
        _historySearchCancellation?.Cancel();
        _historyDocuments = [];
        _historyReady = false;
        _historyFailure = "Saved meetings could not be loaded. Protected data may be unavailable. Reopen Biscotti after checking the storage error.";
        UpdateTransferControls();
        MeetingList.ItemsSource = null;
        OpenMeetingButton.IsEnabled = false;
        HistoryStatus.Text = _historyFailure;
        HistoryStatus.Visibility = Visibility.Visible;
    }

    internal async Task ApplyHistorySearchAsync(Guid? selectedId = null, bool debounce = true)
    {
        if (SearchBox is null || HistoryStatus is null) return;
        _historySearchCancellation?.Cancel();
        var query = SearchBox.Text;
        ClearSearchButton.IsEnabled = query.Length > 0;
        if (!_historyReady)
        {
            HistoryStatus.Text = _historyFailure ?? "Loading saved meetings…";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _historySearchCancellation = cancellation;
        var token = cancellation.Token;
        selectedId ??= SelectedHistoryMeeting?.Id;
        var documents = _historyDocuments;
        HistoryStatus.Text = string.IsNullOrWhiteSpace(query) ? "Loading saved meetings…" : "Searching saved meetings…";
        MeetingList.ItemsSource = null;
        try
        {
            if (debounce) await Task.Delay(200, token);
            var results = await Task.Run(() => MeetingHistorySearch.Search(documents, query, token), token);
            if (token.IsCancellationRequested) return;
            var view = new ListCollectionView(results);
            if (string.IsNullOrWhiteSpace(query)) view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MeetingHistoryResult.DateGroup)));
            MeetingList.ItemsSource = view;
            MeetingList.SelectedItem = results.FirstOrDefault(r => r.Meeting.Id == selectedId);
            HistoryStatus.Text = results.Count == 0
                ? documents.Length == 0 ? "No meetings yet. Record your first meeting to see its transcript, summary and notes here."
                    : "No matching meetings. Try another phrase or clear search."
                : string.IsNullOrWhiteSpace(query) ? $"{results.Count} {(results.Count == 1 ? "meeting" : "meetings")} · newest first. Select a meeting and press Enter to open."
                    : $"{results.Count} matching {(results.Count == 1 ? "meeting" : "meetings")} · title matches first. Select a meeting and press Enter to open.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
            {
                MeetingList.ItemsSource = null;
                HistoryStatus.Text = "Search could not finish. Clear search or reopen Biscotti to retry.";
            }
        }
        finally
        {
            if (ReferenceEquals(_historySearchCancellation, cancellation)) _historySearchCancellation = null;
        }
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await ApplyHistorySearchAsync();

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void HistorySearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            UpdateLayout();
            var top = HistoryCard.TransformToAncestor(MainScroll).Transform(new Point()).Y;
            MainScroll.ScrollToVerticalOffset(MainScroll.VerticalOffset + top - 12);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin)
        {
            SearchBox.Clear();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin && MeetingList.Items.Count > 0)
        {
            MeetingList.SelectedIndex = 0;
            MeetingList.Focus();
            MeetingList.UpdateLayout();
            (MeetingList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }
}
