using System.Windows;
using System.Windows.Controls;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

public partial class CalendarConnectionWindow
{
    private List<CalendarConnection> _connections = [];
    private bool _preview;
    private readonly CancellationTokenSource _closed = new();
    public IReadOnlyList<CalendarConnection> Connections => _connections;

    private async void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not Button { Tag: string provider }) return;
        var dialog = new CalendarProviderWindow(provider, _preview) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Connection is not { } connection) return;
        if (_connections.Any(c => c.Provider == connection.Provider && (c.FeedUrl == connection.FeedUrl
            && string.Equals(c.AppleAccount, connection.AppleAccount, StringComparison.OrdinalIgnoreCase))))
        {
            ConnectionStatus.Text = "That calendar connection is already listed.";
            return;
        }
        _connections.Add(connection);
        await LoadAsync(resetSelection: true);
    }

    private ICalendarSource BuildSource()
    {
        var sources = _connections.Select(c => (c.Label, CalendarProviders.Create(c))).ToList();
        if (_useOutlook) sources.Add(("Classic Outlook", _outlook));
        if (!string.IsNullOrWhiteSpace(_path)) sources.Add(("Local export", new IcsCalendarSource(_path)));
        return new CompositeCalendarSource(sources);
    }

    private void ShowSources()
    {
        ConnectedSources.Children.Clear();
        foreach (var connection in _connections.ToArray())
            AddSourceRow($"{connection.Label} · {CalendarFeedSource.ProviderLabel(connection.Provider)}", () => _connections.Remove(connection));
        if (_useOutlook) AddSourceRow("Classic Outlook", () => _useOutlook = false);
        if (!string.IsNullOrWhiteSpace(_path)) AddSourceRow($"Local export: {System.IO.Path.GetFileName(_path)}", () => _path = null);
    }

    private void AddSourceRow(string title, Action remove)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var button = new Button { Content = "Disconnect", IsEnabled = !_preview, Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(button, Dock.Right);
        button.Click += async (_, _) => { if (_loading) return; remove(); await LoadAsync(resetSelection: false); };
        row.Children.Add(button);
        row.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        ConnectedSources.Children.Add(row);
    }
}
