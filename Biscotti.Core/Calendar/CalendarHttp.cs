using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Biscotti.Core.Calendar;

internal static class CalendarHttp
{
    internal static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(25) };

    internal static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        const int maximumBytes = 10 * 1024 * 1024;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Calendar response is too large.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + count > maximumBytes) throw new InvalidDataException("Calendar response is too large.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    internal static XDocument ParseXml(string value)
    {
        using var input = new StringReader(value);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = 10 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
}

public sealed class CalendarFeedSource(CalendarConnection connection, HttpClient? httpClient = null) : ICalendarSource
{
    public static Uri ValidateUrl(string provider, string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) throw new ArgumentException("Use an HTTPS calendar subscription link.");
        var allowed = provider switch
        {
            "google" => uri.IdnHost == "calendar.google.com" && uri.AbsolutePath.StartsWith("/calendar/ical/", StringComparison.Ordinal),
            "microsoft" => uri.IdnHost is "outlook.office365.com" or "outlook.office.com" or "outlook.live.com",
            _ => false
        };
        if (!allowed || !uri.AbsolutePath.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the calendar provider's iCalendar (.ics) subscription link.");
        return uri;
    }

    public async Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        using var owned = httpClient is null ? CalendarHttp.CreateClient() : null;
        var client = httpClient ?? owned!;
        var uri = ValidateUrl(connection.Provider, connection.FeedUrl);
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/calendar"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                if (redirects >= 3 || response.Headers.Location is null) throw new HttpRequestException("Calendar redirect could not be followed.");
                uri = ValidateUrl(connection.Provider, new Uri(uri, response.Headers.Location).AbsoluteUri);
                continue;
            }
            var text = await CalendarHttp.ReadAsync(response, ct);
            var schedule = IcsCalendarReader.ReadContent(text, from, until, $"provider:{connection.Id}");
            return new(schedule.Calendars.Select(c => c with { Title = connection.Label, Source = ProviderLabel(connection.Provider) }).ToArray(),
                schedule.Events.Select(e => e with { CalendarTitle = connection.Label }).ToArray());
        }
    }

    public static string ProviderLabel(string provider) => provider switch
    {
        "google" => "Google Calendar", "microsoft" => "Outlook / Microsoft 365", "icloud" => "Apple iCloud", _ => "Calendar"
    };
}
