# Calendar context and tray offers

**Status:** Superseded by [Windows calendar parity](calendar-parity-windows.md).

## Decision

Keep the user-selected local `.ics` file as the only calendar source. Selecting a file is explicit consent, and the UI explains that Biscotti reads it locally and sends no calendar data to a provider. Calendar notifications are an opt-in setting, disabled by default; upcoming event and recent meeting navigation appear in the tray when a local calendar is configured. A calendar tray action refreshes the file and resolves the occurrence by its stable event ID before starting. Summary prompts may receive a bounded event description and location only after the user explicitly starts that event; those fields and attendee names are treated as untrusted metadata, and invitees are never evidence of attendance. Calendar offers are derived from the current upcoming list, deduplicated by occurrence ID, and expired or cancelled events disappear on refresh. Any OS notification is generic and only opens Biscotti, which refreshes the calendar before presenting actions.

## Alternatives

- Provider sign-in or a remote calendar feed would introduce calendar network access and expose private account or feed information.
- Persisting calendar descriptions and locations in meeting history is unnecessary for prompting and would retain extra private metadata.
- Calendar notifications enabled by default could surprise users with notifications about private events.

## Why

The existing local `.ics` source preserves the app's local-only boundary while providing recurrence and time-zone handling. Fresh resolution before a tray start prevents stale menu entries from recording against cancelled or expired events. Bounded, untrusted context improves local summaries without implying that invitees attended or allowing event text to direct the model.
