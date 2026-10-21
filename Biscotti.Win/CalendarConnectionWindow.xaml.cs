using System.Windows;
using System.Windows.Controls;
using Biscotti.Core;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

public partial class CalendarConnectionWindow : Window
{
    private readonly ICalendarSource _outlook;
    private readonly List<CheckBox> _choices = new();
    private readonly AppSettings _settings;
    private string? _path;
    private bool _useOutlook;
    private bool _loading;
    public string? FilePath => _path;
    public bool UseOutlook => _useOutlook;
    private List<string> _unavailableEnabled = [];
    public List<string>? SelectedIds => _settings.EnabledCalendarIds is null && _choices.All(c => c.IsChecked == true)
        ? null : _choices.Where(c => c.IsChecked == true).Select(c => (string)c.Tag).Concat(_unavailableEnabled).Distinct().ToList();

    public CalendarConnectionWindow(AppSettings settings, ICalendarSource outlook, bool designPreview = false,
        IReadOnlyList<CalendarConnection>? connections = null)
    {
        InitializeComponent();
        _settings = settings;
        _preview = designPreview;
        _connections = connections?.ToList() ?? [];
        Closed += (_, _) => _closed.Cancel();
        _outlook = outlook;
        _path = settings.CalendarFilePath;
        _useOutlook = settings.UseOutlookCalendar;
        if (designPreview)
        {
            Populate(new([new("preview", "Work", "Synthetic Outlook account")], []), ["preview"]);
            ConnectionStatus.Text = "Synthetic preview · no calendar account opened";
            OutlookButton.IsEnabled = FileButton.IsEnabled = SaveConnectionButton.IsEnabled = false;
        }
        else if (_useOutlook || !string.IsNullOrWhiteSpace(_path) || _connections.Count > 0) Loaded += async (_, _) => await LoadAsync(resetSelection: false);
    }

    private async void Outlook_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _preview) return;
        _useOutlook = true;
        await LoadAsync(resetSelection: true);
    }

    private async void File_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || _preview) return;
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a local calendar export", Filter = "iCalendar files (*.ics)|*.ics", CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true) return;
        _path = picker.FileName;
        await LoadAsync(resetSelection: true);
    }

    private async Task LoadAsync(bool resetSelection)
    {
        _loading = true;
        GoogleButton.IsEnabled = MicrosoftButton.IsEnabled = ICloudButton.IsEnabled = false;
        OutlookButton.IsEnabled = FileButton.IsEnabled = SaveConnectionButton.IsEnabled = false;
        ConnectionStatus.Text = "Refreshing connected calendars…";
        var previousChoices = _choices.ToDictionary(c => (string)c.Tag, c => c.IsChecked == true);
        CalendarChoices.Children.Clear();
        _choices.Clear();
        ShowSources();
        try
        {
            var source = BuildSource();
            var now = DateTimeOffset.Now;
            var schedule = await source.ReadAsync(now, now + CalendarQuery.UpcomingWindow, _closed.Token);
            if (!IsLoaded) return;
            _unavailableEnabled = schedule.Warnings is { Count: > 0 }
                ? (_settings.EnabledCalendarIds ?? []).Except(schedule.Calendars.Select(c => c.Id)).ToList() : [];
            Populate(schedule, resetSelection ? null : _settings.EnabledCalendarIds);
            foreach (var choice in _choices)
                if (previousChoices.TryGetValue((string)choice.Tag, out var selected)) choice.IsChecked = selected;
            ConnectionStatus.Text = schedule.Warnings is { Count: > 0 } ? string.Join("\n", schedule.Warnings)
                : $"{schedule.Calendars.Count} calendars connected · refreshes every minute";
            ShowSources();
            SaveConnectionButton.IsEnabled = schedule.Calendars.Count > 0
                || (!_useOutlook && string.IsNullOrWhiteSpace(_path) && _connections.Count == 0);
        }
        catch (Exception ex)
        {
            ConnectionStatus.Text = ex is TimeoutException
                ? "Outlook is taking too long. Check for an Outlook prompt, then retry or choose a local .ics file."
                : ex is System.IO.IOException ? "The calendar file could not be read. Check the file and try again."
                : "Could not connect. Open classic Outlook with an account, or choose a valid local .ics export.";
        }
        finally
        {
            _loading = false;
            GoogleButton.IsEnabled = MicrosoftButton.IsEnabled = ICloudButton.IsEnabled = true;
            OutlookButton.IsEnabled = FileButton.IsEnabled = true;
        }
    }

    private void Populate(CalendarSchedule schedule, IReadOnlyCollection<string>? selected)
    {
        foreach (var calendar in schedule.Calendars)
        {
            var choice = new CheckBox { Content = $"{calendar.Title} · {calendar.Source}", Tag = calendar.Id,
                IsChecked = selected is null || selected.Contains(calendar.Id), Margin = new Thickness(0, 6, 0, 6) };
            _choices.Add(choice);
            CalendarChoices.Children.Add(choice);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) { if (!_loading) DialogResult = true; }
}
