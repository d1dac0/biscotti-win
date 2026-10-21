using System.Net;
using System.Text;
using Biscotti.Core.Calendar;

namespace Biscotti.Tests;

[Collection("MeetingStore")]
public sealed class CalendarProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);
    private const string EventText = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:provider-event\r\nDTSTART:20261021T160000Z\r\nDTEND:20261021T170000Z\r\nSUMMARY:Provider meeting\r\nURL:https://meet.google.com/abc-defg-hij\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    [Theory]
    [InlineData("google", "https://calendar.google.com/calendar/ical/example/private-test/basic.ics")]
    [InlineData("microsoft", "https://outlook.office365.com/owa/calendar/example/calendar.ics")]
    public async Task SubscriptionProvidersReadEventsWithoutSendingLocalMeetingData(string provider, string url)
    {
        var reads = 0;
        using var http = Client(request =>
        {
            reads++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            return Text("\uFEFF" + EventText);
        });
        var source = new CalendarFeedSource(new("account", provider, "Work", FeedUrl: url), http);
        var schedule = await source.ReadAsync(Now, Now.AddDays(7));
        Assert.Equal(1, reads);
        Assert.Equal("Work", Assert.Single(schedule.Calendars).Title);
        Assert.Equal("Provider meeting", Assert.Single(CalendarQuery.Upcoming(schedule, Now)).Title);
    }

    [Theory]
    [InlineData("google", "https://calendar.google.com.evil.test/calendar/ical/basic.ics")]
    [InlineData("google", "http://calendar.google.com/calendar/ical/basic.ics")]
    [InlineData("google", "https://calendar.google.com:8443/calendar/ical/basic.ics")]
    [InlineData("microsoft", "https://outlook.office365.com.evil.test/calendar.ics")]
    [InlineData("microsoft", "https://user:pass@outlook.live.com/calendar.ics")]
    [InlineData("microsoft", "file:///C:/private.ics")]
    public void SubscriptionAddressesCannotEscapeTheirProvider(string provider, string url) =>
        Assert.Throws<ArgumentException>(() => CalendarFeedSource.ValidateUrl(provider, url));

    [Fact]
    public async Task RedirectDoesNotLeakPrivateCalendarLinkToAnotherHost()
    {
        var requests = 0;
        using var http = Client(_ =>
        {
            requests++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.test/calendar.ics");
            return response;
        });
        var source = new CalendarFeedSource(new("g", "google", "Work", FeedUrl:
            "https://calendar.google.com/calendar/ical/example/private-test/basic.ics"), http);
        await Assert.ThrowsAsync<ArgumentException>(() => source.ReadAsync(Now, Now.AddDays(7)));
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task ICloudDiscoversCalendarsAndOnlySendsReadRequests()
    {
        var calls = 0;
        using var http = Client(request =>
        {
            calls++;
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Contains(request.Method.Method, new[] { "PROPFIND", "REPORT" });
            return calls switch
            {
                1 => Text("<d:multistatus xmlns:d='DAV:'><d:current-user-principal><d:href>/123/principal/</d:href></d:current-user-principal></d:multistatus>", 207),
                2 => Text("<d:multistatus xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'><c:calendar-home-set><d:href>https://p01-caldav.icloud.com/123/calendars/</d:href></c:calendar-home-set></d:multistatus>", 207),
                3 => Text("<d:multistatus xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'><d:response><d:href>/123/calendars/work/</d:href><d:propstat><d:prop><d:displayname>Work</d:displayname><d:resourcetype><c:calendar/></d:resourcetype></d:prop></d:propstat></d:response></d:multistatus>", 207),
                4 => Text($"<d:multistatus xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'><c:calendar-data><![CDATA[{EventText}]]></c:calendar-data></d:multistatus>", 207),
                _ => throw new InvalidOperationException()
            };
        });
        var source = new ICloudCalendarSource(new("apple", "icloud", "iCloud", AppleAccount: "example@example.test", ApplePassword: "test-test-test-test"), http);
        var schedule = await source.ReadAsync(Now, Now.AddDays(7));
        Assert.Equal(4, calls);
        Assert.Equal("Work", Assert.Single(schedule.Calendars).Title);
        Assert.Equal("Provider meeting", Assert.Single(schedule.Events).Title);
        Assert.Equal(schedule.Calendars[0].Id, schedule.Events[0].CalendarId);
    }

    [Fact]
    public async Task ICloudRejectsDiscoveryThatWouldSendCredentialsToAnotherHost()
    {
        var calls = 0;
        using var http = Client(_ =>
        {
            calls++;
            return Text("<d:multistatus xmlns:d='DAV:'><d:current-user-principal><d:href>https://evil.test/principal/</d:href></d:current-user-principal></d:multistatus>", 207);
        });
        var source = new ICloudCalendarSource(new("apple", "icloud", "iCloud", AppleAccount: "example@example.test", ApplePassword: "test-test-test-test"), http);
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ReadAsync(Now, Now.AddDays(7)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void CalDavXmlRejectsExternalEntities() => Assert.Throws<System.Xml.XmlException>(() =>
        CalendarHttp.ParseXml("<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///C:/private'>]><x>&ext;</x>"));

    [Fact]
    public void CredentialsAreEncryptedAndRemovedOnDisconnect()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"biscotti-providers-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "connections.dpapi");
        try
        {
            var store = new CalendarConnectionStore(path);
            var connection = new CalendarConnection("account", "google", "Work", FeedUrl:
                "https://calendar.google.com/calendar/ical/test/private-SYNTHETICSECRET/basic.ics");
            store.Save([connection]);
            Assert.DoesNotContain("SYNTHETICSECRET", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(connection.FeedUrl, Assert.Single(store.Load()).FeedUrl);
            Assert.DoesNotContain("SYNTHETICSECRET", connection.ToString());
            store.Save([]);
            Assert.Empty(store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }

    [Fact]
    public async Task FailedProviderLeavesOtherCalendarsAvailableWithActionableStatus()
    {
        using var good = Client(_ => Text(EventText));
        using var bad = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var connection = new CalendarConnection("g", "google", "Work", FeedUrl: "https://calendar.google.com/calendar/ical/example/private-test/basic.ics");
        var source = new CompositeCalendarSource([
            ("Work", new CalendarFeedSource(connection, good)), ("Unavailable calendar", new CalendarFeedSource(connection with { Id = "other" }, bad))]);
        var result = await source.ReadAsync(Now, Now.AddDays(7));
        Assert.Single(result.Events);
        Assert.Contains("Open Calendars", Assert.Single(result.Warnings!));
        Assert.DoesNotContain("private-test", string.Join("", result.Warnings!));
    }

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new Handler(handler));
    private static HttpResponseMessage Text(string text, int status = 200) => new((HttpStatusCode)status) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
