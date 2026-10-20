# Local iCalendar file source for meeting prompts

**Status:** Accepted

## Decision

Calendar awareness uses a user-selected local `.ics` file. Biscotti Win stores
only its path in `settings.json`, reads the file on the user's machine, and
does not download calendar data, sign in to a calendar provider, or copy the
calendar file into app storage. Upcoming events are shown in the main window
with an explicit Record action. Starting from an event seeds the meeting title
and stores the event's attendee names with that meeting so the local Ollama
summary can use them as possible speaker-name matches. Names may be matched
only when the transcript gives evidence; an attendee roster alone does not
prove attendance. Transcript labels are not renamed from roster membership
alone.

## Alternatives

- Microsoft Graph: deferred because account sign-in and calendar network
  requests conflict with the accepted local-only data boundary.
- ICS URL subscription: deferred because it requires calendar network access
  and may expose a private feed URL.
- A home-grown ICS parser: rejected because recurrence, exclusions, and time
  zone rules require standards-aware parsing.

## Why

The local file supports recurring and time-zoned events while keeping calendar
content on-device. Users can export or sync the file through their calendar
client; Biscotti Win itself makes no calendar network requests. Attendee names
can improve the local model's guesses without treating an invite as proof that
a person spoke.
