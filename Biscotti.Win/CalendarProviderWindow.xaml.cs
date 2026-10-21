using System.Text.RegularExpressions;
using System.Windows;
using Biscotti.Core.Calendar;

namespace Biscotti.Win;

public partial class CalendarProviderWindow : Window
{
    private readonly string _provider;
    private readonly bool _designPreview;
    private bool _checking;
    private readonly CancellationTokenSource _closed = new();
    public CalendarConnection? Connection { get; private set; }

    public CalendarProviderWindow(string provider, bool designPreview = false)
    {
        InitializeComponent();
        _provider = provider;
        _designPreview = designPreview;
        ProviderHeading.Text = CalendarFeedSource.ProviderLabel(provider);
        CalendarLabel.Text = ProviderHeading.Text;
        SetupInstructions.Text = provider switch
        {
            "google" => "In Google Calendar, open Settings → select your calendar → Integrate calendar → copy Secret address in iCal format. Paste the link below. If your work account hides this setting, use a local export instead.",
            "microsoft" => "In Outlook on the web, open Settings → Calendar → Shared calendars → Publish a calendar. Copy its ICS link. Publishing lets anyone with the link read the selected details. If sharing is not permitted, use classic Outlook or a local export instead.",
            "icloud" => "At account.apple.com, open Sign-In and Security → App-Specific Passwords and generate a password for Biscotti. Enter your Apple account and that password below. Two-factor authentication must be enabled on the Apple account.",
            _ => throw new ArgumentException("Unsupported calendar provider.")
        };
        FeedFields.Visibility = provider == "icloud" ? Visibility.Collapsed : Visibility.Visible;
        AppleFields.Visibility = provider == "icloud" ? Visibility.Visible : Visibility.Collapsed;
        Closed += (_, _) => _closed.Cancel();
        if (designPreview) ProviderStatus.Text = "Synthetic preview · no provider requests or saved credentials.";
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_checking) return;
        if (_designPreview) { ProviderStatus.Text = "Preview only · no provider account contacted."; return; }
        var label = IcsCalendarReader.Clean(CalendarLabel.Text, 100) ?? CalendarFeedSource.ProviderLabel(_provider);
        CalendarConnection connection;
        try
        {
            if (_provider == "icloud")
            {
                var password = ApplePassword.Password.Trim();
                if (!Regex.IsMatch(password, "^[A-Za-z0-9]{4}(-[A-Za-z0-9]{4}){3}$") || string.IsNullOrWhiteSpace(AppleAccount.Text))
                    throw new ArgumentException("Enter the Apple email and an app-specific password in the xxxx-xxxx-xxxx-xxxx format.");
                connection = new(Guid.NewGuid().ToString("N"), _provider, label,
                    AppleAccount: AppleAccount.Text.Trim(), ApplePassword: password);
            }
            else
            {
                var uri = CalendarFeedSource.ValidateUrl(_provider, FeedUrl.Password);
                connection = new(Guid.NewGuid().ToString("N"), _provider, label, FeedUrl: uri.AbsoluteUri);
            }
        }
        catch (ArgumentException ex) { ProviderStatus.Text = ex.Message; return; }
        _checking = true;
        ConnectProviderButton.IsEnabled = false;
        ProviderStatus.Text = "Checking this calendar connection…";
        try
        {
            ICalendarSource source = connection.Provider == "icloud" ? new ICloudCalendarSource(connection) : new CalendarFeedSource(connection);
            var now = DateTimeOffset.Now;
            await source.ReadAsync(now, now + CalendarQuery.UpcomingWindow, _closed.Token);
            if (!IsLoaded || _closed.IsCancellationRequested) return;
            Connection = connection;
            FeedUrl.Clear();
            ApplePassword.Clear();
            DialogResult = true;
        }
        catch (OperationCanceledException) { if (IsLoaded) ProviderStatus.Text = "Calendar connection cancelled or timed out. Try again."; }
        catch { ProviderStatus.Text = "Could not connect. Check the subscription link or app-specific password and your internet connection. Your details have not been saved."; }
        finally { _checking = false; ConnectProviderButton.IsEnabled = true; }
    }
}
