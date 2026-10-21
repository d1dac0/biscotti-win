# Connect your calendars

Open **Calendars…** above Upcoming. Choose a provider, complete its setup,
select the calendars you want to show, and choose **Save calendars**.
You can add multiple providers and calendars. No developer account or app
registration is needed.

| Source | What to enter | Notes |
| --- | --- | --- |
| Google Calendar | Secret address in iCal format | One subscription link per calendar; some work accounts disable this setting. |
| Outlook.com / Microsoft 365 | Published calendar ICS link | Your organization may disable publishing. Anyone with the link can read the details you publish. |
| Apple iCloud | Apple account email and an app-specific password | Requires Apple two-factor authentication. Biscotti only reads calendar data. |
| Classic Outlook | Connect classic Outlook | Uses the accounts and calendar folders configured in classic Outlook on this PC. |
| Local calendar file | A local `.ics` export | Works offline. Export it again to receive later changes. |

## Google Calendar

1. On a computer, open Google Calendar Settings and select the calendar.
2. Under **Integrate calendar**, copy **Secret address in iCal format**.
3. Choose **Google Calendar** in Biscotti and paste the address into the
   hidden subscription-link field. Give it a name and choose **Connect calendar**.

Do not make a private Google calendar public to connect it. If a work account
does not expose the secret address, use a permitted export or classic Outlook.
[Google's subscription instructions](https://support.google.com/calendar/answer/37648?hl=en).

## Outlook.com and Microsoft 365

1. In Outlook on the web, open **Settings → Calendar → Shared calendars**.
2. Under **Publish a calendar**, choose the calendar and the details you are
   allowed to share, then copy the **ICS** link.
3. Choose **Outlook / Microsoft 365** in Biscotti and paste that link.

Publishing allows anyone holding the link to read the published calendar.
Use this connection only when that sharing is acceptable. If your employer
blocks publishing or you do not want to publish, choose classic Outlook or
a local file instead. A browser calendar page URL is not a subscription link.
[Microsoft's calendar sharing instructions](https://support.microsoft.com/en-gb/outlook/sharing/share-an-outlook-calendar-as-view-only-with-others).

## Apple iCloud

1. At **account.apple.com**, open **Sign-In and Security → App-Specific Passwords**.
2. Generate a password for Biscotti. Do not enter your primary Apple password.
3. Choose **Apple iCloud** in Biscotti, enter your Apple account email and the
   generated password, and choose **Connect calendar**.

[Apple's app-specific password instructions](https://support.apple.com/en-us/102654).
Changing the main Apple account password can revoke app-specific passwords;
reconnect in Biscotti when access stops working.

## Schedule, preview, and recording

Upcoming shows the next seven days of timed meetings with either a recognized
video-conference link or at least two invitees. All-day entries and ended or
cancelled events are excluded. Click a compact row to preview details, join a
supported conference, or record. An event action rechecks the source before
using it. Biscotti does not start a recording automatically.

For an untargeted recording, a current or imminent meeting can supply initial
context from the most recently refreshed schedule. Its association is visible
in the recording window. **Link event / Change event** lists timed events
starting within 90 minutes of the recording start; **Remove link** removes
calendar context. This does not rename confirmed speakers or imply an invitee
actually attended. Existing meeting titles and notes survive association changes.

The app rechecks sources every minute and has a **Refresh** action. Subscription
providers can delay updates in their published feeds; a local file updates only
when its contents change. Cloud sources need internet. A provider failure is shown
above Upcoming and does not hide events from working sources.

## Privacy and disconnecting

Google and Microsoft subscription links grant calendar access and should be
treated like passwords. Biscotti hides them in setup and saves them, together
with iCloud app-specific credentials, in a DPAPI-protected file under
`%LOCALAPPDATA%\BiscottiWin\Calendar`. They are not copied to meeting exports.
Only recognized provider HTTPS endpoints are used for synchronization.

Provider connections fetch schedules from the chosen service. Audio, transcripts,
notes, summaries and models are never sent to a calendar service. A linked event
snapshot is stored with its meeting in the existing protected meeting storage so
local summary retries retain their calendar context.

Use **Disconnect** beside a source in Calendars, then **Save calendars**, or use
**Remove** above Upcoming to remove all connections. This deletes saved access
from Biscotti. Revoke/reset the subscription URL or app-specific password at the
provider too if you want that credential to stop working everywhere.

Google/Microsoft OAuth sign-in buttons are not implemented. Those require
Biscotti-owned application registrations; this guided setup does not ask users
to create developer applications.
