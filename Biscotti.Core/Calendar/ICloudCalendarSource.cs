using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Biscotti.Core.Calendar;

/// <summary>iCloud CalDAV discovery and bounded read-only reports; never writes calendar resources.</summary>
public sealed partial class ICloudCalendarSource(CalendarConnection connection, HttpClient? httpClient = null) : ICalendarSource
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    [GeneratedRegex(@"^p\d+-caldav\.icloud\.com$", RegexOptions.IgnoreCase, 100)]
    private static partial Regex ICloudHost();

    public static Uri ValidateServiceUri(Uri baseUri, string href)
    {
        var uri = new Uri(baseUri, href);
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0
            || (uri.IdnHost != "caldav.icloud.com" && !ICloudHost().IsMatch(uri.IdnHost)))
            throw new InvalidDataException("The calendar server returned an unsupported service address.");
        return uri;
    }

    public async Task<CalendarSchedule> ReadAsync(DateTimeOffset from, DateTimeOffset until, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connection.AppleAccount) || string.IsNullOrWhiteSpace(connection.ApplePassword))
            throw new InvalidOperationException("An Apple account and app-specific password are required.");
        using var owned = httpClient is null ? CalendarHttp.CreateClient() : null;
        var client = httpClient ?? owned!;
        var root = new Uri("https://caldav.icloud.com/");
        var principalResult = await QueryAsync(client, root, "PROPFIND", "0", new XElement(Dav + "propfind",
            new XElement(Dav + "prop", new XElement(Dav + "current-user-principal"))), ct);
        var principal = ValidateServiceUri(root, Href(principalResult, Dav + "current-user-principal"));
        var homeResult = await QueryAsync(client, principal, "PROPFIND", "0", new XElement(Dav + "propfind",
            new XElement(Dav + "prop", new XElement(CalDav + "calendar-home-set"))), ct);
        var home = ValidateServiceUri(principal, Href(homeResult, CalDav + "calendar-home-set"));
        var calendarResult = await QueryAsync(client, home, "PROPFIND", "1", new XElement(Dav + "propfind",
            new XElement(Dav + "prop", new XElement(Dav + "displayname"), new XElement(Dav + "resourcetype"))), ct);
        var calendars = new List<CalendarInfo>();
        var events = new List<CalendarMeeting>();
        foreach (var response in calendarResult.Descendants(Dav + "response").Where(r => r.Descendants(CalDav + "calendar").Any()).Take(128))
        {
            var uri = ValidateServiceUri(home, response.Element(Dav + "href")?.Value ?? throw new InvalidDataException());
            var title = IcsCalendarReader.Clean(response.Descendants(Dav + "displayname").FirstOrDefault()?.Value, 160) ?? "iCloud calendar";
            var id = $"icloud:{connection.Id}:{uri.AbsolutePath}";
            calendars.Add(new(id, title, "Apple iCloud"));
            var report = new XElement(CalDav + "calendar-query", new XElement(Dav + "prop", new XElement(CalDav + "calendar-data")),
                new XElement(CalDav + "filter", new XElement(CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                    new XElement(CalDav + "comp-filter", new XAttribute("name", "VEVENT"), new XElement(CalDav + "time-range",
                        new XAttribute("start", from.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")),
                        new XAttribute("end", until.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")))))));
            var data = await QueryAsync(client, uri, "REPORT", "1", report, ct);
            var entries = data.Descendants(CalDav + "calendar-data").ToArray();
            if (entries.Length > 2000) throw new InvalidDataException("The calendar contains too many events for this window.");
            foreach (var entry in entries)
            {
                var parsed = IcsCalendarReader.ReadContent(entry.Value, from, until, id);
                events.AddRange(parsed.Events.Select(e => e with { CalendarId = id, CalendarTitle = title }));
            }
        }
        return new(calendars, events.DistinctBy(e => e.EventId).OrderBy(e => e.StartsAt).ToArray());
    }

    private static string Href(XDocument document, XName property) =>
        document.Descendants(property).Descendants(Dav + "href").FirstOrDefault()?.Value ?? throw new InvalidDataException("Calendar discovery did not return a service address.");

    private async Task<XDocument> QueryAsync(HttpClient client, Uri uri, string method, string depth, XElement body, CancellationToken ct)
    {
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), uri);
            var secret = Encoding.UTF8.GetBytes($"{connection.AppleAccount}:{connection.ApplePassword}");
            try { request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(secret)); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret); }
            request.Headers.Add("Depth", depth);
            request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/xml");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                if (redirects >= 3 || response.Headers.Location is null) throw new HttpRequestException("Calendar discovery redirect failed.");
                uri = ValidateServiceUri(uri, response.Headers.Location.OriginalString);
                continue;
            }
            return CalendarHttp.ParseXml(await CalendarHttp.ReadAsync(response, ct));
        }
    }
}
