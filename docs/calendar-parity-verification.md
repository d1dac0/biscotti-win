# Calendar functionality and UI (#109)

## Scope and reference

The macOS flow was reviewed in scosman/Biscotti at
`273e3ff10cab1b14314685da697bc5ac53eb19d1`: CalendarService,
AppShellView, UpcomingEventRow, CalendarInfoCard and EventPickerSheet.
Windows implements the same seven-day meeting schedule, compact upcoming
navigation, event preview/join/record flow, current/imminent association and
nearby-event correction. Windows resources are independently authored WPF.

The user expanded the work to major providers and chose self-managed setup.
Google/Microsoft use subscription URLs; iCloud uses an app-specific password.
This differs from EventKit and does not claim Google/Microsoft OAuth sign-in.
Provider publication policies and feed update delays remain relevant.
See [setup](calendar-setup.md) and the [decision](specs/calendar-parity-windows.md).

## Automated verification

Baseline at `a9bbbc8`: build/check passed (188 Core tests, 1 WPF flow test).
Implementation build/check passed (219 Core tests, 1 WPF flow test, none skipped).
Existing obsolete NAudio and inexact test-read warnings remain.

Coverage includes current and recurring occurrences over seven days, DST,
cancelled/expired events, source/calendar selection, nearby-event ranking,
best-match choice, source changes during refresh, stale-offer clearing,
safe conference URLs, provider subscription host/redirect restrictions,
UTF-8 BOM feeds, iCloud discovery and read-only requests, XML entity rejection,
partial provider failures, DPAPI credential round trips and disconnecting,
snapshot serialization, failed-save rollback and preserved speaker/title edits.

WPF flow checks exercise stale-event rejection and fresh-event resolution,
preview isolation, dismissed previews during delayed Record/Join resolution,
empty pickers, all three provider setup screens, calendar
link/change/remove and save-failure states, and disabling association changes
while processing. Wide/narrow layout checks keep the calendar schedule usable.

## Rendered inspection

Synthetic WPF windows were launched by the native flow test and rendered to
local PNGs with `BISCOTTI_CALENDAR_PREVIEWS` set to an external artifact folder.
The images were inspected: wide upcoming sidebar, narrow stacked schedule,
light/dark event previews, provider setup, narrow connection scrolling, and
linked-meeting controls. Images contain only synthetic meeting data and are
not committed. No live provider credentials or private schedules were used.

Manual preview commands from this worktree root:

```powershell
dotnet run --project Biscotti.Win -c Release --no-build -- --design-preview=calendar
dotnet run --project Biscotti.Win -c Release --no-build -- --design-preview=calendar-event
dotnet run --project Biscotti.Win -c Release --no-build -- --design-preview=calendar-connect
dotnet run --project Biscotti.Win -c Release --no-build -- --design-preview=calendar-google
dotnet run --project Biscotti.Win -c Release --no-build -- --design-preview=calendar-microsoft
dotnet run --project Biscotti.Win -c Release --no-build -- --design-preview=calendar-icloud
```

## Skipped live checks

Live Google/Microsoft subscription authorization and Apple account access were
not tested: those require the user's chosen feed or app-specific credentials.
Classic Outlook was detected on the Windows machine, but its private calendars
were not read. A Mac runtime capture, live call recording and provider-policy
changes were not performed. Provider implementations were checked with bounded
synthetic HTTP/CalDAV responses rather than real account data.
