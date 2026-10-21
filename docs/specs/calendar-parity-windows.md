# Calendar parity on Windows (#109)

**Status:** Accepted for the user-requested calendar parity work, 2026-10-21.

## Decision

Provide explicit read-only connections for Google Calendar, Microsoft 365/Outlook.com, and Apple iCloud, plus locally installed classic Outlook and local .ics fallback. This scope was explicitly expanded by the user on 2026-10-21. Provider synchronization fetches schedules over HTTPS only after connection; audio, transcripts, summaries, notes and models are never sent to calendar services.

The user requested self-managed provider setup without developer registrations. Google and Microsoft connections therefore use user-provided iCalendar subscription URLs rather than OAuth sign-in. Guide users to the provider's subscription settings. A Google secret iCal address and an Outlook published ICS address are bearer credentials: encrypt the complete URL with DPAPI, never show it in normal status/logs, and remove it when disconnected. Explain that Outlook publishing lets link holders access the calendar; if workplace policy forbids publishing, offer classic Outlook or a local export. iCloud uses CalDAV with the user's Apple account and an app-specific password, never the primary account password. The app sends read-only PROPFIND/REPORT requests and protects credentials with DPAPI. Multiple connections can coexist. Provider refresh is subject to the provider's subscription-feed update delay.

Ordinary Google/Microsoft account sign-in requires Biscotti-owned OAuth registrations; do not borrow client IDs or make end users register developer applications. OAuth is not required by this implementation and is not claimed as implemented. Provider requests, redirects and CalDAV discovery URLs stay on supported HTTPS service hosts. Enforce response, redirect and occurrence bounds; expose connection failures without leaking credential-bearing addresses.

Read classic Outlook through its COM object model on a background STA; serialize queries, bound recurrence windows and materialization, release COM references, and never quit or modify Outlook. Default and nested calendar folders from each Outlook store are supported. New Outlook and browser calendars use their Google/Microsoft subscription connection rather than an assumed local Windows calendar database. Existing local file settings migrate without another access request.

Use a shared event model and calendar selection, with a seven-day upcoming window. Upcoming contains timed meetings (recognized conference URL or at least two invitees), excludes cancelled/ended events, sorts chronologically and preserves currently running events. Nearby event pickers include any timed event starting within 90 minutes of a recording, matching the Mac correction workflow. Refresh on connection, startup and every minute, serialize refreshes and reject stale query results after source changes. An explicit event action re-fetches and re-resolves its occurrence before recording or joining. URLs open only after a user action, with HTTPS conference links on recognized exact domains; arbitrary protocols and lookalike hosts are rejected.

Show compact upcoming rows and a separate event preview with time range, calendar, platform, invitees, location and description. Recording remains an explicit action. Seed the initial recording title and choose a best matching current/imminent meeting for an otherwise untargeted start, with visible and removable association. Users can link/change/remove an event during recording or later; this only changes calendar context, never asserted attendance or existing speaker names.

Persist the selected event snapshot in the meeting's existing DPAPI-protected JSON so a later summary retry uses the same context. Do not copy complete source calendars, credentials or raw feeds. Invitees and bounded event text remain untrusted summary context. Transfer/export behavior stays explicitly scoped to its existing schema. Existing generic, opt-in OS notifications remain private; support all-meeting/video-only/never filtering and suppress during recording or after an event ends. Use the same fresh event list for tray actions.

## Alternatives and why

- Windows Appointments is not a reliable EventKit replacement for the unpackaged WPF distribution or new Outlook calendars. Repackaging would not create a universal synchronized calendar store.
- ICS-only and classic-Outlook-only integrations do not satisfy the expanded user request for the major providers. Explicit provider connections add inbound calendar synchronization while retaining on-device recording and AI.
- ICS-only access cannot provide the live local calendar experience requested; retain it as an honest fallback rather than the only source.
- Generic notifications remain a deliberate Windows privacy adaptation; full event details are shown inside the app.

## Sources

- https://learn.microsoft.com/en-us/office/vba/outlook/concepts/getting-started/using-visual-basic-to-automate-outlook
- https://learn.microsoft.com/en-us/office/client-developer/outlook/pia/how-to-search-and-obtain-appointments-in-a-time-range
- https://support.microsoft.com/en-us/office/feature-comparison-between-new-outlook-and-classic-outlook-de453583-1e76-48bf-975a-2e9cd2ee16dd
- https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-supported-api
- Mac reference: CalendarService, AppShellView, CalendarInfoCard and EventPickerSheet in scosman/Biscotti (source reviewed 2026-10-21).
